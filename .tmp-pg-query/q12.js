const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const rid = '6faa6a77-fe21-4ba0-9201-b348c64087ea';
  const st = await c.query(`SELECT "ApprovalStatus", COUNT(*)::int n FROM "Meals" WHERE "RestaurantId"=$1 AND "IsDeleted"=false GROUP BY 1 ORDER BY 1`, [rid]);
  console.log('meal statuses', st.rows);
  const ings = await c.query(`SELECT COUNT(*)::int n,
    COUNT(*) FILTER (WHERE "OriginCountryCode" IS NULL OR trim("OriginCountryCode")='')::int missingOrigin,
    COUNT(*) FILTER (WHERE NOT "IsActive")::int inactive
    FROM "Ingredients" WHERE "RestaurantId"=$1 AND "IsDeleted"=false`, [rid]);
  console.log('ings', ings.rows[0]);
  const sampleIng = await c.query(`SELECT "NameEn","OriginCountryCode","IsActive","MealIconUrl","AllergenCodes" FROM "Ingredients" WHERE "RestaurantId"=$1 AND "IsDeleted"=false LIMIT 5`, [rid]);
  console.log('sampleIng', sampleIng.rows);
  const snap = await c.query(`SELECT "MealsJson"::text AS j FROM "SubscriptionRestaurantDaySnapshots" WHERE "SubscriptionId"='ce88713d-94e5-4f08-a636-d5ee187ace34' AND "RestaurantId"=$1 AND "ServiceDate"='2026-08-29' LIMIT 1`, [rid]);
  const meals = JSON.parse(snap.rows[0].j);
  console.log('snapMealCount', meals.length);
  console.log('first', JSON.stringify(meals[0], null, 2).slice(0,1500));
  const withIng = meals.filter(m => (m.ingredients||[]).length>0).length;
  console.log('withIngredients', withIng, 'avgIng', meals.reduce((a,m)=>a+(m.ingredients||[]).length,0)/meals.length);
  // pool restaurants all
  const pool = await c.query(`SELECT DISTINCT srs."RestaurantId", srs."NameEn",
    (SELECT COUNT(*) FROM "Meals" m WHERE m."RestaurantId"=srs."RestaurantId" AND m."IsDeleted"=false) meals,
    (SELECT COUNT(*) FROM "Ingredients" i WHERE i."RestaurantId"=srs."RestaurantId" AND i."IsDeleted"=false) ings
    FROM "SubscriptionRestaurantSnapshots" srs WHERE srs."SubscriptionId"='ce88713d-94e5-4f08-a636-d5ee187ace34'`);
  console.log('pool', pool.rows);
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
