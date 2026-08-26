const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const cols = await c.query(`SELECT column_name, is_nullable, column_default, data_type FROM information_schema.columns WHERE table_name='MealIngredients' ORDER BY ordinal_position`);
  console.log(cols.rows);
  const sample = await c.query(`SELECT * FROM "MealIngredients" LIMIT 1`);
  console.log('sample keys', Object.keys(sample.rows[0]||{}));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
