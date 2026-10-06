using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StudentCourseManagement
{
    public class Models
    {
        public record Student(int Id, string Name, string Email, bool IsActive);

        public record Course(int Id, string Name, bool IsActive);

        public record EnrollmentView
            (
                int EnrollmentId,
                int StudentId,
                string StudentName,
                bool StudentIsActive,
                int CourseId,
                string CourseName,
                bool CourseIsActive,
                string Semester
            );

        public class EnrollmentFilter
        {
            public int? StudentId { get; init; }
            public string? StudentName { get; init; }
            public int? CourseId { get; init; }
            public string? CourseName { get; init; }
            public string? Semester { get; init; }
            public bool ActiveOnly { get; init; }
        }
    }
}
