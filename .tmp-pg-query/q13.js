const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const rid = '6faa6a77-fe21-4ba0-9201-b348c64087ea';
  const rows = await c.query(`
    SELECT m."Code", m."NameEn", m."Slot",
      array_agg(i."NameEn" ORDER BY mi."SortOrder") AS ingredients
    FROM "Meals" m
    JOIN "MealIngredients" mi ON mi."MealId"=m."Id"
    JOIN "Ingredients" i ON i."Id"=mi."IngredientId"
    WHERE m."RestaurantId"=$1 AND m."IsDeleted"=false AND m."Code" LIKE 'MM20%'
    GROUP BY m."Id", m."Code", m."NameEn", m."Slot"
    ORDER BY m."Code"
  `, [rid]);
  for (const r of rows.rows) console.log(r.Code, r.Slot, r.NameEn, '=>', r.ingredients.join(', '));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
