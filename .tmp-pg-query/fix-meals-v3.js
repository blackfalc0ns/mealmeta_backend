const { Client } = require('pg');
const crypto = require('crypto');

const POOL_RESTAURANTS = [
  '6faa6a77-fe21-4ba0-9201-b348c64087ea',
  'c2f5a474-7582-4895-868a-f5b49d5cc0d2',
  'e8437318-af44-431c-b549-4a841ff3248b',
  '23913ccd-bca7-4eb9-992b-6018e9fcb72e',
];

const RECIPES = {
  'egg & avocado breakfast': {
    pick: ['egg', 'avocado', 'spinach'],
    allergens: 'EGGS',
    descriptionAr: 'بيض طازج مع أفوكادو وسبانخ.',
    descriptionEn: 'Fresh eggs with avocado and spinach.',
  },
  'grilled chicken with rice': {
    pick: ['chicken breast', 'white rice', 'broccoli'],
    allergens: '',
    descriptionAr: 'صدر دجاج مشوي مع رز أبيض وبروكلي.',
    descriptionEn: 'Grilled chicken breast with white rice and broccoli.',
  },
  'greek yogurt with oats': {
    pick: ['yogurt', 'oats'],
    allergens: 'MILK,GLUTEN',
    descriptionAr: 'زبادي يوناني مع شوفان.',
    descriptionEn: 'Greek yogurt with oats.',
  },
  'salmon with seasonal vegetables': {
    pick: ['salmon', 'broccoli', 'zucchini'],
    allergens: 'FISH',
    descriptionAr: 'سالمون مشوي مع بروكلي وكوسة.',
    descriptionEn: 'Grilled salmon with broccoli and zucchini.',
  },
};

const norm = (s) => String(s || '').toLowerCase().replace(/\s+/g, ' ').trim();
const splitCodes = (v) => String(v || '').split(',').map((x) => x.trim()).filter(Boolean);

function toFrozen(i) {
  return {
    Id: i.Id,
    NameAr: i.NameAr,
    NameEn: i.NameEn,
    OriginCountryCode: i.OriginCountryCode,
    SupplierName: i.SupplierName,
    IsLocal: i.IsLocal,
    IsHalalCertified: i.IsHalalCertified,
    AllergenCodes: splitCodes(i.AllergenCodes),
    IsOriginVisibleToCustomers: i.IsOriginVisibleToCustomers,
  };
}

async function main() {
  const c = new Client({
    host: process.env.PGHOST,
    port: +process.env.PGPORT,
    user: process.env.PGUSER,
    password: process.env.PGPASSWORD,
    database: process.env.PGDATABASE,
    ssl: { rejectUnauthorized: false },
  });
  await c.connect();

  // 1) Fix catalog (autocommit)
  for (const [sql, params] of [
    [`UPDATE "Ingredients" SET "AllergenCodes"='EGGS' WHERE lower("NameEn") IN ('egg','eggs') AND COALESCE("AllergenCodes",'')='' AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
    [`UPDATE "Ingredients" SET "AllergenCodes"='MILK' WHERE lower("NameEn") IN ('yogurt','cheddar') AND COALESCE("AllergenCodes",'')='' AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
    [`UPDATE "Ingredients" SET "AllergenCodes"='FISH' WHERE lower("NameEn") IN ('salmon','tuna') AND COALESCE("AllergenCodes",'')='' AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
    [`UPDATE "Ingredients" SET "AllergenCodes"='GLUTEN' WHERE lower("NameEn")='oats' AND COALESCE("AllergenCodes",'')='' AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
    [`UPDATE "Ingredients" SET "OriginCountryCode"=COALESCE(NULLIF(trim("OriginCountryCode"),''),'KW'), "IsActive"=true, "IsOriginVisibleToCustomers"=true, "IsHalalCertified"=true, "SupplierName"=COALESCE(NULLIF(trim("SupplierName"),''),'MealMate Demo Supply') WHERE "RestaurantId"=ANY($1::uuid[]) AND "IsDeleted"=false`, [POOL_RESTAURANTS]],
  ]) {
    const r = await c.query(sql, params);
    console.log('ing update', r.rowCount);
  }

  let mealsFixed = 0;
  let linksCreated = 0;
  for (const rid of POOL_RESTAURANTS) {
    const ings = (await c.query(`SELECT "Id","NameEn" FROM "Ingredients" WHERE "RestaurantId"=$1 AND "IsDeleted"=false`, [rid])).rows;
    const byKey = new Map();
    for (const ing of ings) {
      byKey.set(norm(ing.NameEn), ing);
      if (norm(ing.NameEn) === 'eggs') byKey.set('egg', ing);
      if (norm(ing.NameEn) === 'egg') byKey.set('eggs', ing);
    }
    const meals = (await c.query(
      `SELECT "Id","NameEn" FROM "Meals" WHERE "RestaurantId"=$1 AND "IsDeleted"=false AND lower("NameEn")=ANY($2::text[])`,
      [rid, Object.keys(RECIPES)]
    )).rows;
    for (const meal of meals) {
      const recipe = RECIPES[norm(meal.NameEn)];
      const resolved = recipe.pick.map((p) => byKey.get(norm(p))).filter(Boolean);
      if (!resolved.length) continue;
      await c.query(`DELETE FROM "MealIngredients" WHERE "MealId"=$1`, [meal.Id]);
      let sort = 1;
      for (const ing of resolved) {
        await c.query(
          `INSERT INTO "MealIngredients" ("Id","MealId","IngredientId","PortionGrams","IsOptional","SortOrder","CreatedAtUtc","IsDeleted")
           VALUES ($1,$2,$3,$4,false,$5,now(),false)`,
          [crypto.randomUUID(), meal.Id, ing.Id, 80 + sort * 20, sort]
        );
        sort++;
        linksCreated++;
      }
      await c.query(
        `UPDATE "Meals" SET "PresentAllergenCodes"=$2,"DescriptionAr"=$3,"DescriptionEn"=$4,
          "AllergensReviewed"=true,"DataResponsibilityConfirmed"=true,"ApprovalStatus"=4,
          "IsAvailableForNewOrders"=true,"ModifiedAtUtc"=now() WHERE "Id"=$1`,
        [meal.Id, recipe.allergens || null, recipe.descriptionAr, recipe.descriptionEn]
      );
      mealsFixed++;
    }
  }
  console.log({ mealsFixed, linksCreated });

  // 2) Build maps
  const allLinks = (await c.query(
    `SELECT mi."MealId", i."Id", i."NameAr", i."NameEn", i."OriginCountryCode", i."SupplierName",
            i."IsLocal", i."IsHalalCertified", i."AllergenCodes", i."IsOriginVisibleToCustomers"
     FROM "MealIngredients" mi
     JOIN "Ingredients" i ON i."Id"=mi."IngredientId"
     JOIN "Meals" m ON m."Id"=mi."MealId"
     WHERE m."RestaurantId"=ANY($1::uuid[]) AND i."IsDeleted"=false AND i."IsActive"=true
     ORDER BY mi."MealId", mi."SortOrder"`,
    [POOL_RESTAURANTS]
  )).rows;
  const byMeal = new Map();
  for (const row of allLinks) {
    if (!byMeal.has(row.MealId)) byMeal.set(row.MealId, []);
    byMeal.get(row.MealId).push(toFrozen(row));
  }
  const mealMeta = (await c.query(
    `SELECT "Id","NameAr","NameEn","DescriptionAr","DescriptionEn","PresentAllergenCodes" FROM "Meals" WHERE "RestaurantId"=ANY($1::uuid[])`,
    [POOL_RESTAURANTS]
  )).rows;
  const metaById = new Map(mealMeta.map((m) => [m.Id, m]));

  function patch(meals) {
    if (!Array.isArray(meals)) return null;
    let hit = false;
    for (const meal of meals) {
      const mealId = meal.MealId || meal.mealId;
      if (!mealId || !byMeal.has(mealId)) continue;
      meal.Ingredients = byMeal.get(mealId);
      const meta = metaById.get(mealId);
      if (meta) {
        meal.NameAr = meta.NameAr;
        meal.NameEn = meta.NameEn;
        meal.DescriptionAr = meta.DescriptionAr;
        meal.DescriptionEn = meta.DescriptionEn;
        meal.PresentAllergenCodes = splitCodes(meta.PresentAllergenCodes);
      }
      hit = true;
    }
    return hit ? meals : null;
  }

  // Only recent active/scheduled subscriptions (last 14 days)
  const recentSubs = (await c.query(
    `SELECT "Id" FROM "Subscriptions"
     WHERE "CreatedAtUtc" > now() - interval '14 days'
       AND "Status" IN (3,4,5)
       AND "FulfillmentCompletedAtUtc" IS NOT NULL`
  )).rows.map((r) => r.Id);
  console.log('recentSubs', recentSubs.length);

  const hasHash = (await c.query(
    `SELECT 1 FROM information_schema.columns WHERE table_name='SubscriptionRestaurantDaySnapshots' AND column_name='ContentHash'`
  )).rowCount > 0;

  const snaps = (await c.query(
    `SELECT "Id","MealsJson"::text AS j FROM "SubscriptionRestaurantDaySnapshots"
     WHERE "RestaurantId"=ANY($1::uuid[]) AND "SubscriptionId"=ANY($2::uuid[])`,
    [POOL_RESTAURANTS, recentSubs]
  )).rows;
  console.log('snaps to patch', snaps.length);

  let snapsUpdated = 0;
  for (const snap of snaps) {
    let parsed;
    try { parsed = JSON.parse(snap.j); } catch { continue; }
    const next = patch(parsed);
    if (!next) continue;
    const json = JSON.stringify(next);
    if (hasHash) {
      const hash = crypto.createHash('sha256').update(json, 'utf8').digest('hex');
      await c.query(`UPDATE "SubscriptionRestaurantDaySnapshots" SET "MealsJson"=$2::jsonb, "ContentHash"=$3 WHERE "Id"=$1`, [snap.Id, json, hash]);
    } else {
      await c.query(`UPDATE "SubscriptionRestaurantDaySnapshots" SET "MealsJson"=$2::jsonb WHERE "Id"=$1`, [snap.Id, json]);
    }
    snapsUpdated++;
    if (snapsUpdated % 50 === 0) console.log('snapsUpdated', snapsUpdated);
  }

  const daySnaps = (await c.query(
    `SELECT "Id","MealsJson"::text AS j FROM "SubscriptionDayMealSnapshots" WHERE "SubscriptionId"=ANY($1::uuid[])`,
    [recentSubs]
  )).rows;
  let daySnapsUpdated = 0;
  for (const snap of daySnaps) {
    let parsed;
    try { parsed = JSON.parse(snap.j); } catch { continue; }
    const next = patch(parsed);
    if (!next) continue;
    await c.query(`UPDATE "SubscriptionDayMealSnapshots" SET "MealsJson"=$2::jsonb WHERE "Id"=$1`, [snap.Id, JSON.stringify(next)]);
    daySnapsUpdated++;
  }

  console.log(JSON.stringify({ ok: true, snapsUpdated, daySnapsUpdated }));
  await c.end();
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
