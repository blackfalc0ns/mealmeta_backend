using Npgsql;
var cs = Environment.GetEnvironmentVariable("CS")!;
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
var programId = Guid.Parse("47a57ca2-91f1-4e82-b1b3-ebdd8230835c");
var memberId = Guid.Parse("47930356-f11a-4ec1-8938-b5e9e4271aa7");

await using (var cmd = new NpgsqlCommand(@"
SELECT ""NameEn"", ""AvailableForFamily"", ""IsActive"", ""Status"" FROM ""Programs"" WHERE ""Id""=@id", conn))
{
  cmd.Parameters.AddWithValue("id", programId);
  await using var r = await cmd.ExecuteReaderAsync();
  while (await r.ReadAsync()) Console.WriteLine($"program={r.GetString(0)} family={r.GetBoolean(1)} active={r.GetBoolean(2)} status={r.GetInt32(3)}");
}

await using (var cmd = new NpgsqlCommand(@"
SELECT b.""Id"", b.""NameEn"", b.""AvailableForFamily"", b.""IsActive"", b.""Status"",
  (SELECT COUNT(*) FROM ""RestaurantProgramBundleOffers"" o WHERE o.""BundleId""=b.""Id"" AND o.""IsActive"") AS offers
FROM ""Bundles"" b
WHERE b.""ProgramId""=@id AND NOT b.""IsDeleted""", conn))
{
  cmd.Parameters.AddWithValue("id", programId);
  await using var r = await cmd.ExecuteReaderAsync();
  while (await r.ReadAsync()) Console.WriteLine($"bundle={r.GetGuid(0)} {r.GetString(1)} family={r.GetBoolean(2)} active={r.GetBoolean(3)} status={r.GetInt32(4)} offers={r.GetInt64(5)}");
}

await using (var cmd = new NpgsqlCommand(@"
SELECT ""DisplayName"", ""ProgramId"", ""BundleId"", ""SelectedTier"", ""DurationDays"", ""SetupStatus""
FROM ""FamilyMembers"" WHERE ""Id""=@id", conn))
{
  cmd.Parameters.AddWithValue("id", memberId);
  await using var r = await cmd.ExecuteReaderAsync();
  while (await r.ReadAsync()) Console.WriteLine($"member={r.GetString(0)} prog={r.GetValue(1)} bundle={r.GetValue(2)} tier={r.GetValue(3)} days={r.GetValue(4)} setup={r.GetInt32(5)}");
}
