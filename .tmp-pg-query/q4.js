const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const subId = '3086dbbd-8373-49f9-a246-f139c3763073';
  const one = await c.query('SELECT "Id", "Status", "IsDeleted", "DeletedAtUtc", "CustomerUserId", "FulfillmentCompletedAtUtc", "PaymentVerifiedAtUtc" FROM "Subscriptions" WHERE "Id"=$1', [subId]);
  console.log('SUB', JSON.stringify(one.rows[0], null, 2));
  const uid = one.rows[0].CustomerUserId;
  const ucols = await c.query(`SELECT column_name FROM information_schema.columns WHERE table_name='Users' AND column_name ILIKE '%phone%' OR (table_name='Users' AND column_name ILIKE '%name%') OR (table_name='Users' AND column_name ILIKE '%email%') ORDER BY 1`);
  console.log('user cols', ucols.rows.map(r=>r.column_name).join(','));
  // try CustomerProfiles
  const tables = await c.query(`SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND (table_name ILIKE '%user%' OR table_name ILIKE '%customer%' OR table_name ILIKE '%phone%') ORDER BY 1`);
  console.log('tables', tables.rows.map(r=>r.table_name).join(', '));
  const d = await c.query('SELECT COUNT(*)::int AS n FROM "SubscriptionDays" WHERE "SubscriptionId"=$1', [subId]);
  console.log('DAYS', d.rows[0].n);
  // find phone via identities
  const phoneCols = await c.query(`SELECT table_name, column_name FROM information_schema.columns WHERE column_name ILIKE '%phone%' AND table_schema='public'`);
  console.log('phone cols', JSON.stringify(phoneCols.rows));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
