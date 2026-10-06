using Dapper;
using MySqlConnector;
using System.Data;
using System.Data.SqlTypes;
using static StudentCourseManagement.Models;

namespace StudentCourseManagement;

public class SchoolRepository
{
    public const int MaxCoursesPerStudent = 3;
    public const int MaxStudentsPerCourse = 8;

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

    private MySqlConnection Open() => new(_connectionString);

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
                            INSERT INTO Students (Name, Email) VALUES (@Name, @Email);
                            SELECT CAST(LAST_INSERT_ID() AS SIGNED);";

        try
        {
            await using var conn = Open();
            return await conn.ExecuteScalarAsync<int>(sql, new { Name = name.Trim(), Email = email.Trim() });
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
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
                            INSERT INTO Courses (Name) VALUES (@Name);
                            SELECT CAST(LAST_INSERT_ID() AS SIGNED);";

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
                "UPDATE Students SET IsActive = 0 WHERE Id = @Id AND IsActive = 1;",
                new { Id = studentId }, tx
            );

        if (changed == 0)
            throw new SqlAlreadyFilledException($"Student {studentId} does not exist or is already inactive.");

        var removed = await conn.ExecuteAsync
            (
                "DELETE FROM Enrollments WHERE StudentId = @Id AND Semester = @Semester;",
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
                "UPDATE Courses SET IsActive = 0 WHERE Id = @Id AND IsActive = 1;",
                new { Id = courseId }, tx
            );

        if (changed == 0)
            throw new SqlAlreadyFilledException($"Course {courseId} does not exist or is already inactive.");

        var removed = await conn.ExecuteAsync
            (
                "DELETE FROM Enrollments WHERE CourseId = @Id AND Semester = @Semester;",
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

        // FOR UPDATE = MySQL's version of UPDLOCK: locks the row until the transaction ends.
        var student = await conn.QuerySingleOrDefaultAsync<Student>
            (
                "SELECT Id, Name, Email, IsActive FROM Students WHERE Id = @Id FOR UPDATE;",
                new { Id = studentId }, tx
            );

        var course = await conn.QuerySingleOrDefaultAsync<Course>
            (
                "SELECT Id, Name, IsActive FROM Courses WHERE Id = @Id FOR UPDATE;",
                new { Id = courseId }, tx
            );

        if (student is null) throw new NullReferenceException($"Student {studentId} does not exist.");
        if (course is null) throw new NullReferenceException($"Course {courseId} does not exist.");
        if (!student.IsActive) throw new Exception($"Student '{student.Name}' is inactive and cannot be enrolled.");
        if (!course.IsActive) throw new Exception($"Course '{course.Name}' is inactive and cannot receive students.");

        var args = new { StudentId = studentId, CourseId = courseId, Semester = CurrentSemester };

        var already = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM Enrollments WHERE StudentId = @StudentId AND CourseId = @CourseId AND Semester = @Semester;",
                args, tx
            );

        if (already > 0)
            throw new SqlAlreadyFilledException($"'{student.Name}' is already enrolled in '{course.Name}' for {CurrentSemester}.");

        var studentCount = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM Enrollments WHERE StudentId = @StudentId AND Semester = @Semester;",
                args, tx
            );

        if (studentCount >= MaxCoursesPerStudent)
            throw new SqlAlreadyFilledException($"'{student.Name}' already has {MaxCoursesPerStudent} courses in {CurrentSemester} (maximum).");

        var courseCount = await conn.ExecuteScalarAsync<int>
            (
                "SELECT COUNT(1) FROM Enrollments WHERE CourseId = @CourseId AND Semester = @Semester;",
                args, tx
            );

        if (courseCount >= MaxStudentsPerCourse)
            throw new SqlAlreadyFilledException($"'{course.Name}' is full: {MaxStudentsPerCourse} students in {CurrentSemester} (maximum).");

        try
        {
            var id = await conn.ExecuteScalarAsync<int>
                (
                @"
                    INSERT INTO Enrollments (StudentId, CourseId, Semester)
                    VALUES (@StudentId, @CourseId, @Semester);
                    SELECT CAST(LAST_INSERT_ID() AS SIGNED);",
                    args, tx
                );

            await tx.CommitAsync();
            return id;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new SqlAlreadyFilledException($"'{student.Name}' is already enrolled in '{course.Name}' for {CurrentSemester}.");
        }
    }

    // ------------------------------------------------------------------
    // general retrieval function
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<EnrollmentView>> QueryEnrollmentsAsync(EnrollmentFilter? filter = null)
    {
        filter ??= new EnrollmentFilter();

        const string sql = @"
                SELECT  e.Id        AS EnrollmentId,
                        s.Id        AS StudentId,
                        s.Name      AS StudentName,
                        s.IsActive  AS StudentIsActive,
                        c.Id        AS CourseId,
                        c.Name      AS CourseName,
                        c.IsActive  AS CourseIsActive,
                        e.Semester  AS Semester
                FROM    Enrollments e
                JOIN    Students s ON s.Id = e.StudentId
                JOIN    Courses  c ON c.Id = e.CourseId
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