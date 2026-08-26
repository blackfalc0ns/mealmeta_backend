const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const uid = '1774d5d3-c7d4-4c37-a3ca-aee661321f4c';
  const stale = 'a40d2dc4-716b-479d-9e70-3733907e3f6a';
  const active = '3086dbbd-8373-49f9-a246-f139c3763073';
  for (const id of [stale, active]) {
    const r = await c.query('SELECT "Id","CustomerUserId","Status","IsDeleted","FulfillmentCompletedAtUtc","PaymentVerifiedAtUtc","CreatedAtUtc","PaidAmount_Amount" FROM "Subscriptions" WHERE "Id"=$1', [id]);
    console.log(id, r.rowCount ? JSON.stringify(r.rows[0]) : 'NOT_IN_DB');
  }
  const all = await c.query(`SELECT "Id","Status","IsDeleted","FulfillmentCompletedAtUtc" IS NOT NULL AS fulfilled, "CreatedAtUtc","PaidAmount_Amount"
    FROM "Subscriptions" WHERE "CustomerUserId"=$1 ORDER BY "CreatedAtUtc" DESC`, [uid]);
  console.log('ALL_FOR_USER', JSON.stringify(all.rows, null, 2));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
