using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramMobileDisplayContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "KeywordsAr",
                table: "Programs",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "KeywordsEn",
                table: "Programs",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql("""
                UPDATE "Programs" AS p
                SET
                    "DescriptionAr" = COALESCE(NULLIF(p."DescriptionAr", ''), v."DescriptionAr"),
                    "DescriptionEn" = COALESCE(NULLIF(p."DescriptionEn", ''), v."DescriptionEn"),
                    "KeywordsAr" = CASE WHEN cardinality(p."KeywordsAr") = 0 THEN v."KeywordsAr" ELSE p."KeywordsAr" END,
                    "KeywordsEn" = CASE WHEN cardinality(p."KeywordsEn") = 0 THEN v."KeywordsEn" ELSE p."KeywordsEn" END
                FROM (VALUES
                    ('PROG_KETO',
                     'برنامج منخفض الكربوهيدرات يعتمد على الدهون الصحية لمساعدتك على الالتزام بنمط الكيتو.',
                     'A low-carbohydrate program built around healthy fats to help you follow a keto lifestyle.',
                     ARRAY['كربوهيدرات منخفضة', 'دهون صحية']::text[], ARRAY['Low carb', 'Healthy fats']::text[]),
                    ('PROG_BALANCED',
                     'وجبات يومية متوازنة تجمع البروتين والكربوهيدرات والدهون بنسب مناسبة لنمط حياة صحي.',
                     'Balanced daily meals combining protein, carbohydrates and fats for a healthy lifestyle.',
                     ARRAY['تغذية متوازنة', 'طاقة يومية']::text[], ARRAY['Balanced nutrition', 'Daily energy']::text[]),
                    ('PROG_ATHLETE',
                     'وجبات محسوبة لدعم النشاط الرياضي والطاقة والتعافي بعد التمرين.',
                     'Calculated meals designed to support athletic performance, energy and workout recovery.',
                     ARRAY['أداء رياضي', 'دعم التعافي']::text[], ARRAY['Athletic performance', 'Recovery support']::text[]),
                    ('PROG_WEIGHT_LOSS',
                     'وجبات محسوبة السعرات تساعدك على خسارة الوزن تدريجيًا مع الحفاظ على تغذية متوازنة.',
                     'Calorie-controlled meals that support gradual weight loss while maintaining balanced nutrition.',
                     ARRAY['سعرات محسوبة', 'نتائج مستدامة']::text[], ARRAY['Calorie controlled', 'Sustainable results']::text[]),
                    ('PROG_MUSCLE_GAIN',
                     'وجبات غنية بالبروتين وموزعة لدعم بناء العضلات والتعافي وتحقيق أهداف التمرين.',
                     'Protein-rich meals distributed to support muscle growth, recovery and training goals.',
                     ARRAY['بروتين أعلى', 'بناء العضلات']::text[], ARRAY['Higher protein', 'Muscle growth']::text[]),
                    ('PROG_LOW_CARB',
                     'خطة مرنة تقلل الكربوهيدرات مع الحفاظ على وجبات متنوعة ومتوازنة وسهلة الالتزام.',
                     'A flexible lower-carbohydrate plan with varied, balanced meals that are easy to follow.',
                     ARRAY['كربوهيدرات أقل', 'خيارات مرنة']::text[], ARRAY['Lower carb', 'Flexible choices']::text[])
                ) AS v("Code", "DescriptionAr", "DescriptionEn", "KeywordsAr", "KeywordsEn")
                WHERE p."Code" = v."Code";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeywordsAr",
                table: "Programs");

            migrationBuilder.DropColumn(
                name: "KeywordsEn",
                table: "Programs");
        }
    }
}
