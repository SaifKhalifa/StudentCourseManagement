using StudentCourseManagement;
using static StudentCourseManagement.Models;

public static class Program
{
    public static async Task Main()
    {
        var connectionString = "Server=localhost;Port=3306;Database=SchoolDb;User=root;Password=saif8808;";

        // One repository per semester. "current" is where all the action happens;
        // "previous" is only used to create some history.
        var current = new SchoolRepository(connectionString, "2026-S2");
        var previous = new SchoolRepository(connectionString, "2026-S1");

        // A random number so emails stay unique each time you run the program.
        var random = new Random();
        var tag = random.Next(10000, 99999);

        // ---- 1. Setup ----
        Console.WriteLine("1. Create data");
        var ahmad = await current.AddStudentAsync("Ahmad", $"ahmad.{tag}@school.test");
        var sara = await current.AddStudentAsync("Sara", $"sara.{tag}@school.test");
        var db = await current.AddCourseAsync("Database Systems");
        var algo = await current.AddCourseAsync("Algorithms");
        var net = await current.AddCourseAsync("Networks");
        var os = await current.AddCourseAsync("Operating Systems");

        try
        {
            await current.AddStudentAsync("Clone", $"ahmad.{tag}@school.test");
            Console.WriteLine("  OK      Duplicate email is rejected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BLOCKED Duplicate email is rejected -> {ex.Message}");
        }

        // History from the previous semester.
        await previous.EnrollAsync(ahmad, db);
        await previous.EnrollAsync(ahmad, net);
        await previous.EnrollAsync(sara, db);

        // ---- 2. Enrollment rules ----
        Console.WriteLine();
        Console.WriteLine("2. Enrollment rules (3 courses per student)");

        await TryEnroll(current, "Ahmad -> Database Systems", ahmad, db);
        await TryEnroll(current, "Ahmad -> Algorithms", ahmad, algo);
        await TryEnroll(current, "Ahmad -> Networks", ahmad, net);
        await TryEnroll(current, "Ahmad -> Operating Systems (4th, must fail)", ahmad, os);
        await TryEnroll(current, "Ahmad -> Database Systems (duplicate, must fail)", ahmad, db);
        await TryEnroll(current, "Sara -> Database Systems", sara, db);

        // ---- 3. Course capacity ----
        Console.WriteLine();
        Console.WriteLine("3. Course capacity (8 students per course)");
        for (var i = 1; i <= 9; i++)
        {
            var filler = await current.AddStudentAsync($"Filler{i}", $"filler{i}.{tag}@school.test");

            var label = $"Filler{i} -> Operating Systems";
            if (i == 9)
                label += " (9th, must fail)";

            await TryEnroll(current, label, filler, os);
        }

        // ---- 4. Queries ----
        Console.WriteLine();
        Console.WriteLine("4. Queries");

        await ShowResults(current, "Who took Database Systems in 2026-S1?",
            new EnrollmentFilter { CourseId = db, Semester = "2026-S1" });

        await ShowResults(current, "Which courses did Ahmad take in 2026-S1?",
            new EnrollmentFilter { StudentId = ahmad, Semester = "2026-S1" });

        // ---- 5. Soft delete ----
        Console.WriteLine();
        Console.WriteLine("5. Soft delete (only the current semester is cleaned up)");

        var removedEnrollments = await current.DeleteStudentAsync(ahmad);
        Console.WriteLine($"  Ahmad deactivated, {removedEnrollments} current enrollment(s) removed");

        await TryEnroll(current, "Enroll inactive Ahmad (must fail)", ahmad, os);

        await ShowResults(current, "Ahmad's remaining history",
            new EnrollmentFilter { StudentId = ahmad });

        removedEnrollments = await current.DeleteCourseAsync(os);
        Console.WriteLine($"  Operating Systems deactivated, {removedEnrollments} current enrollment(s) removed");

        await TryEnroll(current, "Sara -> inactive Operating Systems (must fail)", sara, os);

        await ShowResults(current, "Active enrollments in the current semester",
            new EnrollmentFilter { Semester = "2026-S2", ActiveOnly = true });
    }

    // Tries to enroll a student in a course.
    // Prints OK if it worked, or BLOCKED with the reason if the repository rejected it.
    private static async Task TryEnroll(SchoolRepository repo, string label, int studentId, int courseId)
    {
        try
        {
            await repo.EnrollAsync(studentId, courseId);
            Console.WriteLine($"  OK      {label}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  BLOCKED {label} -> {ex.Message}");
        }
    }

    // Runs a query and prints each row it returns.
    private static async Task ShowResults(SchoolRepository repo, string question, EnrollmentFilter filter)
    {
        Console.WriteLine();
        Console.WriteLine($"  Q: {question}");

        var rows = await repo.QueryEnrollmentsAsync(filter);
        if (rows.Count == 0)
        {
            Console.WriteLine("     (no results)");
            return;
        }

        foreach (var row in rows)
        {
            var note = "";
            if (!row.StudentIsActive)
                note += " [student inactive]";
            if (!row.CourseIsActive)
                note += " [course inactive]";

            Console.WriteLine($"     {row.Semester}  {row.StudentName,-10} {row.CourseName}{note}");
        }
    }
}
