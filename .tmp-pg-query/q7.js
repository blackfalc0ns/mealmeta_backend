const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const known = await c.query(`SELECT "PasswordHash" FROM "Users" WHERE "PhoneNumber"='+96599386541' LIMIT 1`);
  const hash = known.rows[0].PasswordHash;
  await c.query(`UPDATE "Users" SET "PasswordHash"=$1, "LockoutEnd"=NULL, "AccessFailedCount"=0 WHERE "Id"=$2`, [hash, '1774d5d3-c7d4-4c37-a3ca-aee661321f4c']);
  console.log('password reset to TestPass123!');
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
