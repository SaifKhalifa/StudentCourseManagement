# Console Output - StudentCourseManagement

Run date: 2026-10-06
Command: `dotnet run`

```
1. Create data
  BLOCKED Duplicate email is rejected -> A student with email 'ahmad.78252@school.test' already exists.

2. Enrollment rules (3 courses per student)
  OK      Ahmad -> Database Systems
  OK      Ahmad -> Algorithms
  OK      Ahmad -> Networks
  BLOCKED Ahmad -> Operating Systems (4th, must fail) -> 'Ahmad' already has 3 courses in 2026-S2 (maximum).
  BLOCKED Ahmad -> Database Systems (duplicate, must fail) -> 'Ahmad' is already enrolled in 'Database Systems' for 2026-S2.
  OK      Sara -> Database Systems

3. Course capacity (8 students per course)
  OK      Filler1 -> Operating Systems
  OK      Filler2 -> Operating Systems
  OK      Filler3 -> Operating Systems
  OK      Filler4 -> Operating Systems
  OK      Filler5 -> Operating Systems
  OK      Filler6 -> Operating Systems
  OK      Filler7 -> Operating Systems
  OK      Filler8 -> Operating Systems
  BLOCKED Filler9 -> Operating Systems (9th, must fail) -> 'Operating Systems' is full: 8 students in 2026-S2 (maximum).

4. Queries

  Q: Who took Database Systems in 2026-S1?
     2026-S1  Ahmad      Database Systems
     2026-S1  Sara       Database Systems

  Q: Which courses did Ahmad take in 2026-S1?
     2026-S1  Ahmad      Database Systems
     2026-S1  Ahmad      Networks

5. Soft delete (only the current semester is cleaned up)
  Ahmad deactivated, 3 current enrollment(s) removed
  BLOCKED Enroll inactive Ahmad (must fail) -> Student 'Ahmad' is inactive and cannot be enrolled.

  Q: Ahmad's remaining history
     2026-S1  Ahmad      Database Systems [student inactive]
     2026-S1  Ahmad      Networks [student inactive]
  Operating Systems deactivated, 8 current enrollment(s) removed
  BLOCKED Sara -> inactive Operating Systems (must fail) -> Course 'Operating Systems' is inactive and cannot receive students.

  Q: Active enrollments in the current semester
     2026-S2  Sara       Database Systems
     2026-S2  Sara       Database Systems
     2026-S2  Sara       Database Systems
```