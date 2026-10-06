CREATE DATABASE SchoolDb;

USE SchoolDb;

CREATE TABLE Students
(
    Id        INT           NOT NULL AUTO_INCREMENT,
    Name      VARCHAR(100)  NOT NULL,
    Email     VARCHAR(256)  NOT NULL,
    IsActive  TINYINT(1)    NOT NULL DEFAULT 1,
    CONSTRAINT PK_Students PRIMARY KEY (Id),
    CONSTRAINT UX_Students_Email UNIQUE (Email),
    CONSTRAINT CK_Students_Name  CHECK (CHAR_LENGTH(TRIM(Name)) > 0),
    CONSTRAINT CK_Students_Email CHECK (Email LIKE '%_@_%')
);

CREATE TABLE Courses
(
    Id        INT           NOT NULL AUTO_INCREMENT,
    Name      VARCHAR(150)  NOT NULL,
    IsActive  TINYINT(1)    NOT NULL DEFAULT 1,
    CONSTRAINT PK_Courses PRIMARY KEY (Id),
    CONSTRAINT CK_Courses_Name CHECK (CHAR_LENGTH(TRIM(Name)) > 0)
);

CREATE TABLE Enrollments
(
    Id         INT          NOT NULL AUTO_INCREMENT,
    StudentId  INT          NOT NULL,
    CourseId   INT          NOT NULL,
    Semester   VARCHAR(20)  NOT NULL,
    CONSTRAINT PK_Enrollments PRIMARY KEY (Id),
    CONSTRAINT FK_Enrollments_Students FOREIGN KEY (StudentId) REFERENCES Students (Id) ON DELETE RESTRICT,
    CONSTRAINT FK_Enrollments_Courses  FOREIGN KEY (CourseId)  REFERENCES Courses  (Id) ON DELETE RESTRICT,
    CONSTRAINT CK_Enrollments_Semester CHECK (CHAR_LENGTH(TRIM(Semester)) > 0),
    CONSTRAINT UQ_Enrollments_Student_Course_Semester UNIQUE (StudentId, CourseId, Semester),

    INDEX IX_Enrollments_Student_Semester (StudentId, Semester),
    INDEX IX_Enrollments_Course_Semester  (CourseId, Semester)
);