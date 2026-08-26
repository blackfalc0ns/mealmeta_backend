const { Client } = require('pg');
(async () => {
  const c = new Client({ host: process.env.PGHOST, port: +process.env.PGPORT, user: process.env.PGUSER, password: process.env.PGPASSWORD, database: process.env.PGDATABASE, ssl: { rejectUnauthorized: false } });
  await c.connect();
  const ids = ['6faa6a77-fe21-4ba0-9201-b348c64087ea','c2f5a474-7582-4895-868a-f5b49d5cc0d2','e8437318-af44-431c-b549-4a841ff3248b','23913ccd-bca7-4eb9-992b-6018e9fcb72e'];
  const r = await c.query(`
    SELECT rp."Id" AS restaurant_id, rp."TradeNameAr", rp."TradeNameEn", u."Email", u."PhoneNumber", u."Id" AS user_id
    FROM "RestaurantProfiles" rp
    JOIN "Users" u ON u."Id"=rp."UserId"
    WHERE rp."Id"=ANY($1::uuid[])
    ORDER BY rp."TradeNameEn"`, [ids]);
  console.log(JSON.stringify(r.rows, null, 2));
  await c.end();
})().catch(e=>{console.error(e);process.exit(1)});
