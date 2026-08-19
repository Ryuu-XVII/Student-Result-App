// This tool copies data one-way: from the production database into the
// testing database. It never writes anything back to production.
//
// It runs on a schedule (see .github/workflows/sync-testing-db.yml), and can
// also be run by hand:
//   DB_SYNC_PASSWORD=xxxx dotnet run --project Tools/DbSync

using Microsoft.Data.SqlClient;

var password = Environment.GetEnvironmentVariable("DB_SYNC_PASSWORD")
    ?? throw new InvalidOperationException("Set the DB_SYNC_PASSWORD environment variable first.");

string ConnectionString(string database) =>
    $"Data Source=student-result-server.database.windows.net,1433;Initial Catalog={database};User ID=db_sync_service;Password={password};Encrypt=True;";

using var prod = new SqlConnection(ConnectionString("Student_Result_App"));
using var test = new SqlConnection(ConnectionString("Student_Result_App_Testing"));

await prod.OpenAsync();
await test.OpenAsync();

// Read everything out of production first.
var modules = new List<(int Id, string Code, string Name, int Year, int Count, string Status)>();
using (var cmd = new SqlCommand("SELECT Id, Code, Name, AcademicYear, StudentCount, Status FROM dbo.Modules", prod))
using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        modules.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5)));
    }
}

var students = new List<(int Id, string StudentNumber, string FullName, string ModuleCode, double Mark)>();
using (var cmd = new SqlCommand("SELECT Id, StudentNumber, FullName, ModuleCode, Mark FROM dbo.Students", prod))
using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        students.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
    }
}

Console.WriteLine($"Read {modules.Count} module(s) and {students.Count} student(s) from production.");

// Now replace everything in testing with what we just read. Students is
// deleted first because it has a foreign key pointing at Modules.
using (var cmd = new SqlCommand("DELETE FROM dbo.Students; DELETE FROM dbo.Modules;", test))
{
    await cmd.ExecuteNonQueryAsync();
}

foreach (var m in modules)
{
    using var cmd = new SqlCommand(
        "SET IDENTITY_INSERT dbo.Modules ON; " +
        "INSERT INTO dbo.Modules (Id, Code, Name, AcademicYear, StudentCount, Status) VALUES (@Id, @Code, @Name, @Year, @Count, @Status); " +
        "SET IDENTITY_INSERT dbo.Modules OFF;",
        test);
    cmd.Parameters.AddWithValue("@Id", m.Id);
    cmd.Parameters.AddWithValue("@Code", m.Code);
    cmd.Parameters.AddWithValue("@Name", m.Name);
    cmd.Parameters.AddWithValue("@Year", m.Year);
    cmd.Parameters.AddWithValue("@Count", m.Count);
    cmd.Parameters.AddWithValue("@Status", m.Status);
    await cmd.ExecuteNonQueryAsync();
}

foreach (var s in students)
{
    using var cmd = new SqlCommand(
        "SET IDENTITY_INSERT dbo.Students ON; " +
        "INSERT INTO dbo.Students (Id, StudentNumber, FullName, ModuleCode, Mark) VALUES (@Id, @Number, @Name, @ModuleCode, @Mark); " +
        "SET IDENTITY_INSERT dbo.Students OFF;",
        test);
    cmd.Parameters.AddWithValue("@Id", s.Id);
    cmd.Parameters.AddWithValue("@Number", s.StudentNumber);
    cmd.Parameters.AddWithValue("@Name", s.FullName);
    cmd.Parameters.AddWithValue("@ModuleCode", s.ModuleCode);
    cmd.Parameters.AddWithValue("@Mark", s.Mark);
    await cmd.ExecuteNonQueryAsync();
}

Console.WriteLine($"Copied {modules.Count} module(s) and {students.Count} student(s) into the testing database.");
