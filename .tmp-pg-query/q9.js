const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const r = await c.query('SELECT s."Id", s."CustomerUserId", u."PhoneNumber" FROM "Subscriptions" s JOIN "Users" u ON u."Id"=s."CustomerUserId" WHERE s."Id"=$1', ['ce88713d-94e5-4f08-a636-d5ee187ace34']);
  console.log(JSON.stringify(r.rows));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
