const { Client } = require('pg');
(async () => {
  const c = new Client({ connectionString: process.env.CS, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const subId = process.env.SUB;
  const one = await c.query('SELECT "Id", "Status", "PaymentStatus", "FulfillmentCompletedAtUtc", "CustomerUserId", "CreatedAtUtc", "DurationDays", "StartDate", "FamilyGroupId" FROM "Subscriptions" WHERE "Id"=$1', [subId]);
  console.log('FOUND', one.rowCount);
  console.log(JSON.stringify(one.rows, null, 2));
  if (one.rowCount) {
    const uid = one.rows[0].CustomerUserId;
    const u = await c.query('SELECT "Id", "Phone", "FullName" FROM "Users" WHERE "Id"=$1', [uid]);
    console.log('USER', JSON.stringify(u.rows));
    const d = await c.query('SELECT COUNT(*)::int AS n FROM "SubscriptionDays" WHERE "SubscriptionId"=$1', [subId]);
    console.log('DAYS', d.rows[0].n);
  } else {
    const recent = await c.query(`SELECT s."Id", s."Status", s."PaymentStatus", s."FulfillmentCompletedAtUtc" IS NOT NULL AS fulfilled, s."CreatedAtUtc", u."Phone"
      FROM "Subscriptions" s LEFT JOIN "Users" u ON u."Id"=s."CustomerUserId"
      ORDER BY s."CreatedAtUtc" DESC NULLS LAST LIMIT 20`);
    console.log('RECENT', JSON.stringify(recent.rows, null, 2));
  }
  await c.end();
})().catch(e => { console.error(e); process.exit(1); });
