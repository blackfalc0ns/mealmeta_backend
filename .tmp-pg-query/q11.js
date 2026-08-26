const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const restaurants = ['6faa6a77-fe21-4ba0-9201-b348c64087ea','c2f5a474-7582-4895-868a-f5b49d5cc0d2'];
  for (const rid of restaurants) {
    const p = await c.query('SELECT "TradeNameEn" FROM "RestaurantProfiles" WHERE "Id"=$1', [rid]);
    const meals = await c.query(`SELECT COUNT(*)::int n, COUNT(*) FILTER (WHERE "ApprovalStatus"=2)::int approved FROM "Meals" WHERE "RestaurantId"=$1 AND "IsDeleted"=false`, [rid]);
    const ings = await c.query(`SELECT COUNT(*)::int n FROM "Ingredients" WHERE "RestaurantId"=$1 AND "IsDeleted"=false`, [rid]);
    const links = await c.query(`SELECT COUNT(*)::int n FROM "MealIngredients" mi JOIN "Meals" m ON m."Id"=mi."MealId" WHERE m."RestaurantId"=$1`, [rid]);
    console.log(p.rows[0]?.TradeNameEn, 'meals', meals.rows[0], 'ingredients', ings.rows[0].n, 'mealIngredientLinks', links.rows[0].n);
  }
  // sample MealsJson ingredients from subscription
  const snap = await c.query(`SELECT "RestaurantId","ServiceDate", LEFT("MealsJson", 800) AS j FROM "SubscriptionRestaurantDaySnapshots" WHERE "SubscriptionId"='ce88713d-94e5-4f08-a636-d5ee187ace34' AND "RestaurantId"='6faa6a77-fe21-4ba0-9201-b348c64087ea' AND "ServiceDate"='2026-08-29' LIMIT 1`);
  console.log('SNAP', snap.rows[0]?.j);
  // parse ingredients presence
  if (snap.rows[0]) {
    const meals = JSON.parse(snap.rows[0].j);
    console.log('mealCount', meals.length);
    console.log('firstMeal keys', Object.keys(meals[0]||{}));
    console.log('first ingredients', JSON.stringify(meals[0]?.ingredients||meals[0]?.Ingredients||null));
    const withIng = meals.filter(m => (m.ingredients||m.Ingredients||[]).length>0).length;
    console.log('mealsWithIngredients', withIng);
  }
  // all restaurants in that sub pool
  const pool = await c.query(`SELECT "RestaurantId","NameEn" FROM "SubscriptionRestaurantSnapshots" WHERE "SubscriptionId"='ce88713d-94e5-4f08-a636-d5ee187ace34'`);
  console.log('POOL', JSON.stringify(pool.rows));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
