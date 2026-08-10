using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArabicCountryCallingCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DialCode",
                table: "Countries",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "+965");

            migrationBuilder.AddColumn<string>(
                name: "FlagEmoji",
                table: "Countries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "🇰🇼");

            migrationBuilder.Sql("""
                INSERT INTO "Countries" (
                    "Id", "Code", "NameAr", "NameEn", "CurrencyCode", "DialCode", "FlagEmoji",
                    "IsActive", "IsDeleted", "CreatedAtUtc"
                )
                VALUES
                    ('019feaf0-0001-7000-8000-000000000001', 'KW', 'الكويت', 'Kuwait', 'KWD', '+965', '🇰🇼', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000002', 'SA', 'السعودية', 'Saudi Arabia', 'SAR', '+966', '🇸🇦', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000003', 'AE', 'الإمارات', 'United Arab Emirates', 'AED', '+971', '🇦🇪', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000004', 'QA', 'قطر', 'Qatar', 'QAR', '+974', '🇶🇦', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000005', 'BH', 'البحرين', 'Bahrain', 'BHD', '+973', '🇧🇭', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000006', 'OM', 'عُمان', 'Oman', 'OMR', '+968', '🇴🇲', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000007', 'EG', 'مصر', 'Egypt', 'EGP', '+20', '🇪🇬', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000008', 'JO', 'الأردن', 'Jordan', 'JOD', '+962', '🇯🇴', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000009', 'LB', 'لبنان', 'Lebanon', 'LBP', '+961', '🇱🇧', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000010', 'SY', 'سوريا', 'Syria', 'SYP', '+963', '🇸🇾', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000011', 'IQ', 'العراق', 'Iraq', 'IQD', '+964', '🇮🇶', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000012', 'PS', 'فلسطين', 'Palestine', 'ILS', '+970', '🇵🇸', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000013', 'YE', 'اليمن', 'Yemen', 'YER', '+967', '🇾🇪', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000014', 'MA', 'المغرب', 'Morocco', 'MAD', '+212', '🇲🇦', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000015', 'DZ', 'الجزائر', 'Algeria', 'DZD', '+213', '🇩🇿', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000016', 'TN', 'تونس', 'Tunisia', 'TND', '+216', '🇹🇳', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000017', 'LY', 'ليبيا', 'Libya', 'LYD', '+218', '🇱🇾', TRUE, FALSE, NOW()),
                    ('019feaf0-0001-7000-8000-000000000018', 'SD', 'السودان', 'Sudan', 'SDG', '+249', '🇸🇩', TRUE, FALSE, NOW())
                ON CONFLICT ("Code") DO UPDATE SET
                    "NameAr" = EXCLUDED."NameAr",
                    "NameEn" = EXCLUDED."NameEn",
                    "CurrencyCode" = EXCLUDED."CurrencyCode",
                    "DialCode" = EXCLUDED."DialCode",
                    "FlagEmoji" = EXCLUDED."FlagEmoji",
                    "IsActive" = TRUE,
                    "IsDeleted" = FALSE,
                    "ModifiedAtUtc" = NOW();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DialCode",
                table: "Countries");

            migrationBuilder.DropColumn(
                name: "FlagEmoji",
                table: "Countries");
        }
    }
}
