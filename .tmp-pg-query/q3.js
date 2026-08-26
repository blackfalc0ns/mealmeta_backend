const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const cols = await c.query(`SELECT column_name FROM information_schema.columns WHERE table_name='Subscriptions' ORDER BY ordinal_position`);
  console.log(cols.rows.map(r => r.column_name).join(', '));
  const subId = '3086dbbd-8373-49f9-a246-f139c3763073';
  const one = await c.query('SELECT * FROM "Subscriptions" WHERE "Id"=$1', [subId]);
  console.log('FOUND', one.rowCount);
  if (one.rowCount) {
    const r = one.rows[0];
    const pick = {};
    for (const k of Object.keys(r)) {
      if (/id|status|fulfill|customer|family|start|duration|created|paid|payment/i.test(k)) pick[k]=r[k];
    }
    console.log(JSON.stringify(pick, null, 2));
    const u = await c.query('SELECT "Id", "Phone", "FullName" FROM "Users" WHERE "Id"=$1', [r.CustomerUserId]);
    console.log('USER', JSON.stringify(u.rows));
    const d = await c.query('SELECT COUNT(*)::int AS n FROM "SubscriptionDays" WHERE "SubscriptionId"=$1', [subId]);
    console.log('DAYS', d.rows[0].n);
  } else {
    const recent = await c.query(`SELECT s."Id", s."Status"::text, s."FulfillmentCompletedAtUtc", s."CreatedAtUtc", u."Phone"
      FROM "Subscriptions" s LEFT JOIN "Users" u ON u."Id"=s."CustomerUserId"
      ORDER BY s."CreatedAtUtc" DESC NULLS LAST LIMIT 20`);
    console.log('RECENT', JSON.stringify(recent.rows, null, 2));
  }
  const good = await c.query('SELECT "Id", "Status"::text, "FulfillmentCompletedAtUtc" FROM "Subscriptions" WHERE "Id"=$1', ['12b69c78-9cf6-42f0-b93b-912fcd2d01d4']);
  console.log('GOOD', JSON.stringify(good.rows));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
