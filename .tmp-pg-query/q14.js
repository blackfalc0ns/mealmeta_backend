const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const rid = '6faa6a77-fe21-4ba0-9201-b348c64087ea';
  const snap = await c.query(`SELECT "MealsJson"::text AS j FROM "SubscriptionRestaurantDaySnapshots" WHERE "SubscriptionId"='ce88713d-94e5-4f08-a636-d5ee187ace34' AND "RestaurantId"=$1 AND "ServiceDate"='2026-08-29' LIMIT 1`, [rid]);
  const meals = JSON.parse(snap.rows[0].j);
  const ids = meals.map(m => m.MealId);
  const dbMeals = await c.query(`SELECT "Id","Code","NameEn","Slot" FROM "Meals" WHERE "Id"=ANY($1::uuid[])`, [ids]);
  console.log('snapshot meals:');
  for (const m of dbMeals.rows) {
    const ings = await c.query(`SELECT i."NameEn" FROM "MealIngredients" mi JOIN "Ingredients" i ON i."Id"=mi."IngredientId" WHERE mi."MealId"=$1 ORDER BY mi."SortOrder"`, [m.Id]);
    const snapM = meals.find(x => x.MealId === m.Id);
    console.log(m.Code, m.Slot, m.NameEn, 'DB=>', ings.rows.map(x=>x.NameEn).join(', '), '| SNAP=>', (snapM.Ingredients||[]).map(x=>x.NameEn).join(', '));
  }
  // count non-MM20 meals
  const counts = await c.query(`SELECT CASE WHEN "Code" LIKE 'MM20%' THEN 'MM20' ELSE 'OTHER' END g, COUNT(*)::int n FROM "Meals" WHERE "RestaurantId"=$1 AND "IsDeleted"=false GROUP BY 1`, [rid]);
  console.log('counts', counts.rows);
  const other = await c.query(`SELECT "Code","NameEn","Slot", (SELECT COUNT(*) FROM "MealIngredients" mi WHERE mi."MealId"=m."Id") links FROM "Meals" m WHERE "RestaurantId"=$1 AND "IsDeleted"=false AND ("Code" IS NULL OR "Code" NOT LIKE 'MM20%') ORDER BY "NameEn" LIMIT 30`, [rid]);
  console.log('OTHER sample', other.rows);
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
