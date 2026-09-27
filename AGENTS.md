# Backend Project Rules — MealMate Backend

@./.agents/skills/zero-mock-data/SKILL.md

## Strict Zero Mock Data Policy

- **ABSOLUTE RULE**: Do not use fake, mocked, placeholder, hardcoded, simulated, fabricated, temporary, or randomly generated business data in production code.
- **Real Sources Only**: All business data returned by the backend must originate from the real persistence layer (EF Core DbContext / PostgreSQL), authentic cache, external API provider, or authenticated session context.
- **No Silent Fake Fallbacks**: If data does not exist, return an empty result, null (where valid), 404, or appropriate domain/validation error. Never fabricate default data.
- **Empty Database = Empty Response**: Never return sample records because a table is empty.
- **No Fake Success / Placeholders**: Never return fake success responses for unfinished logic.
- **Code Review Check**: Changed code must be checked for forbidden terms (`mock`, `fake`, `dummy`, `sample`, `testdata`, `placeholder`, `John Doe`, etc.).
- **Final Confirmation**: Every backend task must conclude with the explicit confirmation block:
  ```text
  Mock Data Check:
  - No mock business data introduced.
  - No fake fallback responses introduced.
  - No hardcoded production business values introduced.
  - All runtime data comes from real application sources.
  ```
