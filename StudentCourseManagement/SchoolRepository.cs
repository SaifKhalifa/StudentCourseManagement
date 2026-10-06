using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.SqlTypes;
using static StudentCourseManagement.Models;

namespace StudentCourseManagement;

public class SchoolRepository
{
    public const int MaxCoursesPerStudent = 3;
    public const int MaxStudentsPerCourse = 8;

    private const int SqlUniqueViolation = 2627;      // UNIQUE constraint
    private const int SqlUniqueIndexViolation = 2601; // unique index

    private readonly string _connectionString;

    /// the semester that limits and cleanups apply to.
    public string CurrentSemester { get; }

    public SchoolRepository(string connectionString, string currentSemester)
    {
        if (string.IsNullOrWhiteSpace(currentSemester))
            throw new ArgumentException("Current semester is required.", nameof(currentSemester));

        _connectionString = connectionString;
        CurrentSemester = currentSemester.Trim();
    }

    private SqlConnection Open() => new(_connectionString);

    // ------------------------------------------------------------------
    // Create
    // ------------------------------------------------------------------

    public async Task<int> AddStudentAsync(string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentNullException("Student name is required.");
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentNullException("Student email is required.");
        }

        const string sql = @"
                            INSERT INTO dbo.Students (Name, Email) VALUES (@Name, @Email);
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        try
        {
            await using var conn = Open();
            return await conn.ExecuteScalarAsync<int>(sql, new { Name = name.Trim(), Email = email.Trim() });
        }
        catch (SqlException ex) when (ex.Number is SqlUniqueViolation or SqlUniqueIndexViolation)
        {
            throw new DuplicateNameException($"A student with email '{email}' already exists.");
        }
    }

    public async Task<int> AddCourseAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentNullException("Course name is required.");
        }

        const string sql = @"
                            INSERT INTO dbo.Courses (Name) VALUES (@Name);
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        await using var conn = Open();
        return await conn.ExecuteScalarAsync<int>(sql, new { Name = name.Trim() });
    }

    // ------------------------------------------------------------------
    // Soft delete (current semester only)
    // ------------------------------------------------------------------

    // Marks the student inactive and removes only their current semester enrollments.
    public async Task<int> DeleteStudentAsync(int studentId)
    {
        await using var conn = Open();
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        
        var changed = await conn.ExecuteAsync
            (
                "UPDATE dbo.Students SET IsActive = 0 WHERE Id = @Id AND IsActive = 1;",
                new { Id = studentId }, tx
            );

        if (changed == 0)
            throw new SqlAlreadyFilledException($"Student {studentId} does not exist or is already inactive.");

        var removed = await conn.ExecuteAsync
            (
                "DELETE FROM dbo.Enrollments WHERE StudentId = @Id AND Semester = @Semester;",
                new { Id = studentId, Semester = CurrentSemester }, tx
            );

        await tx.CommitAsync();
        return removed;
    }

    public async Task<int> DeleteCourseAsync(int courseId)
    {
        await using var conn = Open();
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var changed = await conn.ExecuteAsync
            (
                "UPDATE dbo.Courses SET IsActive = 0 WHERE Id = @Id AND IsActive = 1;",
                new { Id = courseId }, tx
            );

        if (changed == 0)
            throw new SqlAlreadyFilledException($"Course {courseId} does not exist or is already inactive.");

        var removed = await conn.ExecuteAsync
            (
                "DELETE FROM dbo.Enrollments WHERE CourseId = @Id AND Semester = @Semester;",
                new { Id = courseId, Semester = CurrentSemester }, tx
            );

        await tx.CommitAsync();
        return removed;
    }

    // ------------------------------------------------------------------
    // Enrollment (current semester)
    // ------------------------------------------------------------------

    public async Task<int> EnrollAsync(int studentId, int courseId)
    {
        await using var conn = Open();
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var student = await conn.QuerySingleOrDefaultAsync<Student>
            (
                "SELECT Id, Name, Email, IsActive FROM dbo.Students WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;",
                new { Id = studentId }, tx
            );

        var course = await conn.QuerySingleOrDefaultAsync<Course>
            (
                "SELECT Id, Name, IsActive FROM dbo.Courses WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;",
                new { Id = courseId }, tx
            );

        if (student is null) throw new NullReferenceException($"Student {studentId} does not exist.");
        if (course is null) throw new NullReferenceException($"Course {courseId} does not exist.");
        if (!student.IsActive) throw new Exception($"Student '{student.Name}' is inactive and cannot be enrolled.");
        if (!course.IsActive) throw new Exception($"Course '{course.Name}' is inactive and cannot receive students.");

        var args = new { StudentId = studentId, CourseId = courseId, Semester = CurrentSemester };

        var already = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM dbo.Enrollments WHERE StudentId = @StudentId AND CourseId = @CourseId AND Semester = @Semester;",
                args, tx
            );
        
        if (already > 0)
            throw new SqlAlreadyFilledException($"'{student.Name}' is already enrolled in '{course.Name}' for {CurrentSemester}.");

        var studentCount = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM dbo.Enrollments WHERE StudentId = @StudentId AND Semester = @Semester;",
                args, tx
            );
        
        if (studentCount >= MaxCoursesPerStudent)
            throw new SqlAlreadyFilledException($"'{student.Name}' already has {MaxCoursesPerStudent} courses in {CurrentSemester} (maximum).");

        var courseCount = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM dbo.Enrollments WHERE CourseId = @CourseId AND Semester = @Semester;",
                args, tx
            );
        
        if (courseCount >= MaxStudentsPerCourse)
            throw new SqlAlreadyFilledException($"'{course.Name}' is full: {MaxStudentsPerCourse} students in {CurrentSemester} (maximum).");

        try
        {
            var id = await conn.ExecuteScalarAsync<int>
                (
                @"
                    INSERT INTO dbo.Enrollments (StudentId, CourseId, Semester)
                    VALUES (@StudentId, @CourseId, @Semester);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    args, tx
                );

            await tx.CommitAsync();
            return id;
        }
        catch (SqlException ex) when (ex.Number is SqlUniqueViolation or SqlUniqueIndexViolation)
        {
            throw new SqlAlreadyFilledException($"'{student.Name}' is already enrolled in '{course.Name}' for {CurrentSemester}.");
        }
    }

    // ------------------------------------------------------------------
    // general retrieval function
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<EnrollmentView>> QueryEnrollmentsAsync(EnrollmentFilter? filter = null)
    {
        filter = new EnrollmentFilter();

        const string sql = @"
                SELECT  e.Id        AS EnrollmentId,
                        s.Id        AS StudentId,
                        s.Name      AS StudentName,
                        s.IsActive  AS StudentIsActive,
                        c.Id        AS CourseId,
                        c.Name      AS CourseName,
                        c.IsActive  AS CourseIsActive,
                        e.Semester  AS Semester
                FROM    dbo.Enrollments e
                JOIN    dbo.Students s ON s.Id = e.StudentId
                JOIN    dbo.Courses  c ON c.Id = e.CourseId
                WHERE   (@StudentId   IS NULL OR s.Id       = @StudentId)
                  AND   (@StudentName IS NULL OR s.Name     = @StudentName)
                  AND   (@CourseId    IS NULL OR c.Id       = @CourseId)
                  AND   (@CourseName  IS NULL OR c.Name     = @CourseName)
                  AND   (@Semester    IS NULL OR e.Semester = @Semester)
                  AND   (@ActiveOnly  = 0     OR (s.IsActive = 1 AND c.IsActive = 1))
                ORDER BY e.Semester, s.Name, c.Name;";

        await using var conn = Open();

        var rows = await conn.QueryAsync<EnrollmentView>(sql, new
        {
            filter.StudentId,
            filter.StudentName,
            filter.CourseId,
            filter.CourseName,
            filter.Semester,
            ActiveOnly = filter.ActiveOnly ? 1 : 0
        });
        return rows.AsList();
    }
}