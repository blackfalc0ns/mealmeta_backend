const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const u = await c.query('SELECT "Id", "PhoneNumber", "PasswordHash", "LockoutEnd", "AccessFailedCount" FROM "Users" WHERE "Id"=$1', ['1774d5d3-c7d4-4c37-a3ca-aee661321f4c']);
  console.log(JSON.stringify({
    Id: u.rows[0].Id,
    PhoneNumber: u.rows[0].PhoneNumber,
    hashPrefix: String(u.rows[0].PasswordHash||'').slice(0,30),
    LockoutEnd: u.rows[0].LockoutEnd,
    AccessFailedCount: u.rows[0].AccessFailedCount
  }, null, 2));
  // copy hash from known working user 99386541
  const known = await c.query(`SELECT "Id", "PhoneNumber", "PasswordHash" FROM "Users" WHERE "PhoneNumber" LIKE '%99386541%' OR "UserName" LIKE '%99386541%' LIMIT 1`);
  console.log('KNOWN', JSON.stringify(known.rows.map(r=>({Id:r.Id,Phone:r.PhoneNumber,hashPrefix:String(r.PasswordHash||'').slice(0,30)}))));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
