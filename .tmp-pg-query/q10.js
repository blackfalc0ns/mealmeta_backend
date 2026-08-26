const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const hash = (await c.query(`SELECT "PasswordHash" FROM "Users" WHERE "PhoneNumber"='+96599386541'`)).rows[0].PasswordHash;
  await c.query(`UPDATE "Users" SET "PasswordHash"=$1, "LockoutEnd"=NULL, "AccessFailedCount"=0 WHERE "Id"=$2`, [hash, '4bb71eee-df3e-4c59-a2a3-0c876eca170b']);
  const snap = await c.query(`SELECT COUNT(*)::int n FROM "SubscriptionRestaurantDaySnapshots" WHERE "SubscriptionId"=$1`, ['ce88713d-94e5-4f08-a636-d5ee187ace34']);
  console.log('daySnapshots', snap.rows[0].n);
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
