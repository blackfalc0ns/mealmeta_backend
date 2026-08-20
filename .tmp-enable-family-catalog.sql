UPDATE ""Programs"" SET ""AvailableForFamily"" = TRUE WHERE NOT ""IsDeleted"" AND ""IsActive"" = TRUE AND ""Status"" = 1;
UPDATE ""Bundles"" SET ""AvailableForFamily"" = TRUE WHERE NOT ""IsDeleted"" AND ""IsActive"" = TRUE AND ""Status"" = 1;
SELECT
  (SELECT COUNT(*) FROM ""Programs"" WHERE ""AvailableForFamily"") AS programs_family,
  (SELECT COUNT(*) FROM ""Bundles"" WHERE ""AvailableForFamily"") AS bundles_family;
