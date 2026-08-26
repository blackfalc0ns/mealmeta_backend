/**
 * Fix wrong MealIngredient links for demo slot meals (DC01/FP01/...),
 * then refresh frozen MealsJson on subscription day snapshots.
 */
const { Client } = require('pg');
const crypto = require('crypto');

const POOL_RESTAURANTS = [
  '6faa6a77-fe21-4ba0-9201-b348c64087ea', // Diet Care
  'c2f5a474-7582-4895-868a-f5b49d5cc0d2', // Fit Plate
  'e8437318-af44-431c-b549-4a841ff3248b', // Green Bowl
  '23913ccd-bca7-4eb9-992b-6018e9fcb72e', // Protein Lab
];

/** Correct recipes by meal NameEn (case-insensitive). */
const RECIPES = {
  'egg & avocado breakfast': {
    ingredients: ['Egg', 'Eggs', 'Avocado', 'Spinach'],
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

function norm(s) {
  return String(s || '')
    .toLowerCase()
    .replace(/breast/g, 'breast')
    .replace(/\s+/g, ' ')
    .trim();
}

function ingredientKey(nameEn) {
  return norm(nameEn).replace(/\s+/g, ' ');
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
  await c.query('BEGIN');

  try {
    // Ensure allergen codes on base ingredients
    const allergenFixes = [
      [`UPDATE "Ingredients" SET "AllergenCodes"='EGGS' WHERE lower("NameEn") IN ('egg','eggs') AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "AllergenCodes"='MILK' WHERE lower("NameEn") IN ('yogurt','cheddar') AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "AllergenCodes"='FISH' WHERE lower("NameEn") IN ('salmon','tuna') AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "AllergenCodes"='CRUSTACEANS' WHERE lower("NameEn")='shrimp' AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "AllergenCodes"='GLUTEN' WHERE lower("NameEn")='oats' AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "AllergenCodes"='TREE_NUTS' WHERE lower("NameEn")='walnut' AND ("AllergenCodes" IS NULL OR "AllergenCodes"='') AND "RestaurantId"=ANY($1::uuid[])`, [POOL_RESTAURANTS]],
      [`UPDATE "Ingredients" SET "OriginCountryCode"=COALESCE(NULLIF(trim("OriginCountryCode"),''),'KW'),
          "IsActive"=true, "IsOriginVisibleToCustomers"=true, "IsHalalCertified"=true,
          "SupplierName"=COALESCE(NULLIF(trim("SupplierName"),''),'MealMate Demo Supply')
        WHERE "RestaurantId"=ANY($1::uuid[]) AND "IsDeleted"=false`, [POOL_RESTAURANTS]],
    ];
    for (const [sql, params] of allergenFixes) {
      await c.query(sql, params);
    }

    let mealsFixed = 0;
    let linksCreated = 0;

    for (const rid of POOL_RESTAURANTS) {
      const ings = await c.query(
        `SELECT "Id","NameEn","NameAr","OriginCountryCode","SupplierName","IsLocal","IsHalalCertified","AllergenCodes","IsOriginVisibleToCustomers","IsActive","MealIconUrl"
         FROM "Ingredients" WHERE "RestaurantId"=$1 AND "IsDeleted"=false`,
        [rid]
      );
      const byKey = new Map();
      for (const ing of ings.rows) {
        byKey.set(ingredientKey(ing.NameEn), ing);
        // aliases
        if (ingredientKey(ing.NameEn) === 'eggs') byKey.set('egg', ing);
        if (ingredientKey(ing.NameEn) === 'egg') byKey.set('eggs', ing);
      }

      const meals = await c.query(
        `SELECT "Id","NameEn","NameAr","Code","Slot","PresentAllergenCodes"
         FROM "Meals"
         WHERE "RestaurantId"=$1 AND "IsDeleted"=false
           AND lower("NameEn") = ANY($2::text[])`,
        [rid, Object.keys(RECIPES)]
      );

      for (const meal of meals.rows) {
        const recipe = RECIPES[norm(meal.NameEn)];
        if (!recipe) continue;

        const resolved = [];
        for (const pick of recipe.pick) {
          const ing = byKey.get(norm(pick));
          if (!ing) {
            console.warn(`MISSING ingredient "${pick}" for restaurant ${rid} meal ${meal.Code}`);
            continue;
          }
          resolved.push(ing);
        }
        if (resolved.length === 0) continue;

        await c.query(`DELETE FROM "MealIngredients" WHERE "MealId"=$1`, [meal.Id]);
        let sort = 1;
        for (const ing of resolved) {
          await c.query(
            `INSERT INTO "MealIngredients"
              ("Id","MealId","IngredientId","PortionGrams","IsOptional","SortOrder","CreatedAtUtc","IsDeleted")
             VALUES ($1,$2,$3,$4,false,$5,now(),false)`,
            [
              crypto.randomUUID(),
              meal.Id,
              ing.Id,
              80 + sort * 20,
              sort,
            ]
          );
          sort++;
          linksCreated++;
        }

        await c.query(
          `UPDATE "Meals" SET
             "PresentAllergenCodes"=$2,
             "DescriptionAr"=COALESCE(NULLIF(trim("DescriptionAr"),''), $3),
             "DescriptionEn"=COALESCE(NULLIF(trim("DescriptionEn"),''), $4),
             "AllergensReviewed"=true,
             "DataResponsibilityConfirmed"=true,
             "ApprovalStatus"=4,
             "IsAvailableForNewOrders"=true,
             "ModifiedAtUtc"=now()
           WHERE "Id"=$1`,
          [meal.Id, recipe.allergens || null, recipe.descriptionAr, recipe.descriptionEn]
        );
        mealsFixed++;
      }
    }

    console.log(JSON.stringify({ mealsFixed, linksCreated }));

    // Refresh MealsJson ingredients on frozen snapshots for these restaurants
    const snaps = await c.query(
      `SELECT "Id","RestaurantId","MealsJson"::text AS j
       FROM "SubscriptionRestaurantDaySnapshots"
       WHERE "RestaurantId"=ANY($1::uuid[])`,
      [POOL_RESTAURANTS]
    );

    let snapsUpdated = 0;
    for (const snap of snaps.rows) {
      let meals;
      try {
        meals = JSON.parse(snap.j);
      } catch {
        continue;
      }
      if (!Array.isArray(meals) || meals.length === 0) continue;

      let changed = false;
      for (const meal of meals) {
        const mealId = meal.MealId || meal.mealId;
        if (!mealId) continue;
        const links = await c.query(
          `SELECT i."Id", i."NameAr", i."NameEn", i."OriginCountryCode", i."SupplierName",
                  i."IsLocal", i."IsHalalCertified", i."AllergenCodes", i."IsOriginVisibleToCustomers"
           FROM "MealIngredients" mi
           JOIN "Ingredients" i ON i."Id"=mi."IngredientId"
           WHERE mi."MealId"=$1 AND i."IsDeleted"=false AND i."IsActive"=true
           ORDER BY mi."SortOrder"`,
          [mealId]
        );
        const frozen = links.rows.map((i) => ({
          Id: i.Id,
          NameAr: i.NameAr,
          NameEn: i.NameEn,
          OriginCountryCode: i.OriginCountryCode,
          SupplierName: i.SupplierName,
          IsLocal: i.IsLocal,
          IsHalalCertified: i.IsHalalCertified,
          AllergenCodes: String(i.AllergenCodes || '')
            .split(',')
            .map((x) => x.trim())
            .filter(Boolean),
          IsOriginVisibleToCustomers: i.IsOriginVisibleToCustomers,
        }));

        const mealRow = await c.query(
          `SELECT "PresentAllergenCodes","DescriptionAr","DescriptionEn","NameAr","NameEn" FROM "Meals" WHERE "Id"=$1`,
          [mealId]
        );
        if (mealRow.rows[0]) {
          const m = mealRow.rows[0];
          meal.NameAr = m.NameAr;
          meal.NameEn = m.NameEn;
          meal.DescriptionAr = m.DescriptionAr;
          meal.DescriptionEn = m.DescriptionEn;
          meal.PresentAllergenCodes = String(m.PresentAllergenCodes || '')
            .split(',')
            .map((x) => x.trim())
            .filter(Boolean);
        }
        meal.Ingredients = frozen;
        changed = true;
      }

      if (!changed) continue;

      const json = JSON.stringify(meals);
      const hash = crypto.createHash('sha256').update(json, 'utf8').digest('hex');
      // keep slot requirements; update meals + content hash if column exists
      const cols = await c.query(
        `SELECT column_name FROM information_schema.columns
         WHERE table_name='SubscriptionRestaurantDaySnapshots' AND column_name IN ('ContentHash','MealsJson')`
      );
      const names = new Set(cols.rows.map((r) => r.column_name));
      if (names.has('ContentHash')) {
        await c.query(
          `UPDATE "SubscriptionRestaurantDaySnapshots"
           SET "MealsJson"=$2::jsonb, "ContentHash"=$3
           WHERE "Id"=$1`,
          [snap.Id, json, hash]
        );
      } else {
        await c.query(
          `UPDATE "SubscriptionRestaurantDaySnapshots" SET "MealsJson"=$2::jsonb WHERE "Id"=$1`,
          [snap.Id, json]
        );
      }
      snapsUpdated++;
    }

    // Also refresh day-level meal snapshots if present
    const daySnaps = await c.query(
      `SELECT "Id","MealsJson"::text AS j FROM "SubscriptionDayMealSnapshots"
       WHERE "SubscriptionId" IN (
         SELECT DISTINCT "SubscriptionId" FROM "SubscriptionRestaurantDaySnapshots"
         WHERE "RestaurantId"=ANY($1::uuid[])
       )`,
      [POOL_RESTAURANTS]
    );
    let daySnapsUpdated = 0;
    for (const snap of daySnaps.rows) {
      let meals;
      try {
        meals = JSON.parse(snap.j);
      } catch {
        continue;
      }
      if (!Array.isArray(meals)) continue;
      for (const meal of meals) {
        const mealId = meal.MealId || meal.mealId;
        if (!mealId) continue;
        const links = await c.query(
          `SELECT i."Id", i."NameAr", i."NameEn", i."OriginCountryCode", i."SupplierName",
                  i."IsLocal", i."IsHalalCertified", i."AllergenCodes", i."IsOriginVisibleToCustomers"
           FROM "MealIngredients" mi
           JOIN "Ingredients" i ON i."Id"=mi."IngredientId"
           WHERE mi."MealId"=$1 AND i."IsDeleted"=false AND i."IsActive"=true
           ORDER BY mi."SortOrder"`,
          [mealId]
        );
        meal.Ingredients = links.rows.map((i) => ({
          Id: i.Id,
          NameAr: i.NameAr,
          NameEn: i.NameEn,
          OriginCountryCode: i.OriginCountryCode,
          SupplierName: i.SupplierName,
          IsLocal: i.IsLocal,
          IsHalalCertified: i.IsHalalCertified,
          AllergenCodes: String(i.AllergenCodes || '')
            .split(',')
            .map((x) => x.trim())
            .filter(Boolean),
          IsOriginVisibleToCustomers: i.IsOriginVisibleToCustomers,
        }));
      }
      await c.query(
        `UPDATE "SubscriptionDayMealSnapshots" SET "MealsJson"=$2::jsonb WHERE "Id"=$1`,
        [snap.Id, JSON.stringify(meals)]
      );
      daySnapsUpdated++;
    }

    await c.query('COMMIT');
    console.log(JSON.stringify({ snapsUpdated, daySnapsUpdated, ok: true }));
  } catch (e) {
    await c.query('ROLLBACK');
    throw e;
  } finally {
    await c.end();
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
