const fs = require('fs');
const { Client } = require('pg');
(async () => {
  const raw = fs.readFileSync('conn.txt', 'utf8').trim();
  const map = Object.fromEntries(raw.split(';').filter(Boolean).map((p) => {
    const i = p.indexOf('=');
    return [p.slice(0, i), p.slice(i + 1)];
  }));
  const c = new Client({
    host: map.Host,
    port: Number(map.Port || 5432),
    database: map.Database,
    user: map.Username || map.User,
    password: map.Password,
    ssl: { rejectUnauthorized: false },
  });
  await c.connect();
  const sub = '3086dbbd-8373-49f9-a246-f139c3763073';
  const date = '2026-08-30';
  const meal = '04e4758a-4210-4b15-a41c-b5908cf7af49';
  const day = await c.query(
    `SELECT "Id","Status","RestaurantId","ResolvedMenuPlanVersionId","IsDeleted"
     FROM "SubscriptionDays" WHERE "SubscriptionId"=$1 AND "ServiceDate"=$2`,
    [sub, date]);
  console.log('DAY', JSON.stringify(day.rows, null, 2));
  if (day.rows.length) {
    const dayId = day.rows[0].Id;
    const sel = await c.query(
      `SELECT "Id","MealId","SortOrder","IsDeleted","DeletedAtUtc","CreatedAtUtc"
       FROM "DailyMealSelections" WHERE "SubscriptionDayId"=$1`,
      [dayId]);
    console.log('SELECTIONS', JSON.stringify(sel.rows, null, 2));
    const same = await c.query(
      `SELECT "Id","IsDeleted" FROM "DailyMealSelections"
       WHERE "SubscriptionDayId"=$1 AND "MealId"=$2`,
      [dayId, meal]);
    console.log('SAME_MEAL', JSON.stringify(same.rows));
  }
  // Also check global query filter for soft deletes - any unique index conflicts
  const idx = await c.query(`SELECT indexname, indexdef FROM pg_indexes WHERE tablename='DailyMealSelections'`);
  console.log('INDEXES', JSON.stringify(idx.rows, null, 2));
  await c.end();
})().catch((e) => { console.error(e); process.exit(1); });
