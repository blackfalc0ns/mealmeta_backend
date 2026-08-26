const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const u = await c.query('SELECT "Id", "PhoneNumber", "UserName", "FullName" FROM "Users" WHERE "Id"=$1', ['1774d5d3-c7d4-4c37-a3ca-aee661321f4c']);
  console.log(JSON.stringify(u.rows));
  const good = await c.query('SELECT "Id","Status","FulfillmentCompletedAtUtc" FROM "Subscriptions" WHERE "Id"=$1', ['12b69c78-9cf6-42f0-b93b-912fcd2d01d4']);
  console.log('GOOD', JSON.stringify(good.rows));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
