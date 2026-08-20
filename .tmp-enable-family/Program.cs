using Npgsql;
var cs = Environment.GetEnvironmentVariable("CS")!;
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
await using (var cmd = new NpgsqlCommand(@"
SELECT
  (SELECT COUNT(*) FROM ""RestaurantProgramBundleOffers"" WHERE ""IsActive"") AS offers,
  (SELECT COUNT(*) FROM ""RestaurantProfiles"" WHERE ""VerificationStatus"" = 4 AND ""IsOperationallyActive"") AS approved_ops,
  (SELECT COUNT(*) FROM ""RestaurantProfiles"") AS restaurants;
", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{ await r.ReadAsync(); Console.WriteLine($"offers={r.GetInt64(0)} approved_ops={r.GetInt64(1)} restaurants={r.GetInt64(2)}"); }

await using (var cmd = new NpgsqlCommand(@"
SELECT p.""NameEn"", COUNT(DISTINCT b.""Id"") AS bundles_with_offers
FROM ""Programs"" p
JOIN ""Bundles"" b ON b.""ProgramId"" = p.""Id"" AND NOT b.""IsDeleted"" AND b.""IsActive"" AND b.""Status"" = 4 AND b.""AvailableForFamily""
JOIN ""RestaurantProgramBundleOffers"" o ON o.""BundleId"" = b.""Id"" AND o.""ProgramId"" = b.""ProgramId"" AND o.""IsActive""
JOIN ""RestaurantProfiles"" r ON r.""Id"" = o.""RestaurantId""
WHERE NOT p.""IsDeleted"" AND p.""IsActive"" AND p.""Status"" = 4 AND p.""AvailableForFamily""
  AND r.""VerificationStatus"" = 4
  AND r.""IsOperationallyActive""
GROUP BY p.""NameEn""
ORDER BY 1;
", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
  var n=0;
  while (await r.ReadAsync()) { Console.WriteLine($"{r.GetString(0)} offers_bundles={r.GetInt64(1)}"); n++; }
  Console.WriteLine($"programs_with_offers={n}");
}

// without restaurant filter
await using (var cmd = new NpgsqlCommand(@"
SELECT COUNT(DISTINCT p.""Id"")
FROM ""Programs"" p
JOIN ""Bundles"" b ON b.""ProgramId"" = p.""Id"" AND b.""AvailableForFamily"" AND b.""IsActive"" AND b.""Status""=4 AND NOT b.""IsDeleted""
JOIN ""RestaurantProgramBundleOffers"" o ON o.""BundleId"" = b.""Id"" AND o.""IsActive""
WHERE p.""AvailableForFamily"" AND p.""IsActive"" AND p.""Status""=4 AND NOT p.""IsDeleted"";
", conn))
Console.WriteLine($"programs_with_any_active_offer={await cmd.ExecuteScalarAsync()}");
