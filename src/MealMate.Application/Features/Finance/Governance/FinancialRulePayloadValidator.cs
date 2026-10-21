using System.Text.Json;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Finance.Governance;

public sealed class FinancialRulePayloadValidator
{
    private sealed record PayloadContract(IReadOnlyDictionary<string, Type> Fields, IReadOnlySet<string> RequiredFields);

    private static readonly IReadOnlyDictionary<string, PayloadContract> Contracts =
        new Dictionary<string, PayloadContract>(StringComparer.Ordinal)
        {
            ["cancel.fee"] = new PayloadContract(new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["maxFees"] = typeof(decimal),
                ["minimumExecutedDays"] = typeof(int),
                ["minimumUnexecutedExclusive"] = typeof(int),
            }, new HashSet<string>(StringComparer.Ordinal)),
            ["CANCELLATION"] = new PayloadContract(new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["minimumExecutedDays"] = typeof(int),
                ["minimumUnexecutedExclusive"] = typeof(int),
            }, new HashSet<string>(["minimumExecutedDays", "minimumUnexecutedExclusive"], StringComparer.Ordinal)),
            ["subscription.pricing"] = new PayloadContract(new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["individualCheckoutEnabled"] = typeof(bool),
                ["familyCheckoutEnabled"] = typeof(bool),
                ["campaignCheckoutEnabled"] = typeof(bool),
                ["restaurantPublicationEnabled"] = typeof(bool),
                ["cashWalletTenderEnabled"] = typeof(bool),
                ["customerGatewayFeeRecoveryEnabled"] = typeof(bool),
                ["referencePriceMaxKwd26"] = typeof(decimal),
                ["customerListPriceMaxKwd"] = typeof(decimal),
                ["maximumAcquisitionLossKwd"] = typeof(decimal),
                ["minimumContributionKwd"] = typeof(decimal),
                ["minimumContributionRate"] = typeof(decimal),
                ["minimumContributionMode"] = typeof(string),
                ["referencePriceCapKind"] = typeof(string),
                ["referencePriceCapUnitBasis"] = typeof(string),
                ["statisticalReviewMethod"] = typeof(string),
                ["influencerCostTreatment"] = typeof(string),
                ["incrementalServiceCostTreatment"] = typeof(string),
                ["campaignCostTreatment"] = typeof(string),
                ["quoteLifetimeSeconds"] = typeof(int),
                ["gatewayFeePct"] = typeof(decimal),
                ["gatewayFeeFixedKwd"] = typeof(decimal),
                ["rMax"] = typeof(decimal),
                ["rMin"] = typeof(decimal),
                ["dShort"] = typeof(int),
                ["dLong"] = typeof(int),
                ["gamma"] = typeof(decimal),
                ["roundingStepKwd"] = typeof(decimal),
                ["supplyCostTreatment"] = typeof(string),
                ["contributionBasis"] = typeof(string),
            }, new HashSet<string>([
                "individualCheckoutEnabled", "familyCheckoutEnabled", "campaignCheckoutEnabled", "restaurantPublicationEnabled",
                "cashWalletTenderEnabled", "customerGatewayFeeRecoveryEnabled", "referencePriceMaxKwd26", "customerListPriceMaxKwd",
                "maximumAcquisitionLossKwd", "minimumContributionKwd", "minimumContributionRate", "minimumContributionMode",
                "referencePriceCapKind", "referencePriceCapUnitBasis", "statisticalReviewMethod", "influencerCostTreatment",
                "incrementalServiceCostTreatment", "campaignCostTreatment", "quoteLifetimeSeconds",
                "gatewayFeePct", "gatewayFeeFixedKwd", "rMax", "rMin",
                "dShort", "dLong", "gamma", "roundingStepKwd", "supplyCostTreatment", "contributionBasis"], StringComparer.Ordinal)),
        };

    public Error? Validate(string ruleKey, int schemaVersion, string payloadJson)
    {
        if (schemaVersion != 1)
        {
            return Error.Validation("finance.rule_schema_version_unsupported");
        }

        if (!Contracts.TryGetValue(ruleKey, out var contract))
        {
            return Error.Validation("finance.rule_contract_unsupported");
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Error.Validation("finance.invalid_payload");
            }

            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length == 0)
            {
                return Error.Validation("finance.invalid_payload");
            }
            if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            {
                return Error.Validation("finance.invalid_payload");
            }

            foreach (var property in properties)
            {
                if (!contract.Fields.TryGetValue(property.Name, out var expectedType) || !MatchesType(property.Value, expectedType))
                {
                    return Error.Validation("finance.invalid_payload");
                }

                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetDecimal(out var value)
                    && value < 0m)
                {
                    return Error.Validation("finance.invalid_payload");
                }
            }

            if (contract.RequiredFields.Any(required => !document.RootElement.TryGetProperty(required, out _)))
            {
                return Error.Validation("finance.invalid_payload");
            }

            if (ruleKey == "subscription.pricing" && !ValidatePricingPolicy(document.RootElement))
                return Error.Validation("finance.invalid_payload");

            return null;
        }
        catch (JsonException)
        {
            return Error.Validation("finance.invalid_payload");
        }
    }

    private static bool MatchesType(JsonElement value, Type expectedType) =>
        expectedType == typeof(decimal)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _)
            : expectedType == typeof(int)
                ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)
                : expectedType == typeof(bool)
                    ? value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    : expectedType == typeof(string)
                        ? value.ValueKind == JsonValueKind.String
                : false;

    private static bool ValidatePricingPolicy(JsonElement payload)
    {
        var enabledRoute = payload.GetProperty("individualCheckoutEnabled").GetBoolean()
            || payload.GetProperty("familyCheckoutEnabled").GetBoolean()
            || payload.GetProperty("campaignCheckoutEnabled").GetBoolean()
            || payload.GetProperty("restaurantPublicationEnabled").GetBoolean();
        var gatewayPct = payload.GetProperty("gatewayFeePct").GetDecimal();
        var rMax = payload.GetProperty("rMax").GetDecimal();
        var rMin = payload.GetProperty("rMin").GetDecimal();
        var gamma = payload.GetProperty("gamma").GetDecimal();
        var dShort = payload.GetProperty("dShort").GetInt32();
        var dLong = payload.GetProperty("dLong").GetInt32();
        var contributionMode = payload.GetProperty("minimumContributionMode").GetString();
        var contributionAmount = payload.GetProperty("minimumContributionKwd").GetDecimal();
        var contributionRate = payload.GetProperty("minimumContributionRate").GetDecimal();
        var capKind = payload.GetProperty("referencePriceCapKind").GetString();
        var capBasis = payload.GetProperty("referencePriceCapUnitBasis").GetString();
        var reviewMethod = payload.GetProperty("statisticalReviewMethod").GetString();
        var influencerTreatment = payload.GetProperty("influencerCostTreatment").GetString();
        var incrementalTreatment = payload.GetProperty("incrementalServiceCostTreatment").GetString();
        var campaignTreatment = payload.GetProperty("campaignCostTreatment").GetString();
        var quoteLifetimeSeconds = payload.GetProperty("quoteLifetimeSeconds").GetInt32();

        return enabledRoute
            && !payload.GetProperty("cashWalletTenderEnabled").GetBoolean()
            && !payload.GetProperty("customerGatewayFeeRecoveryEnabled").GetBoolean()
            && payload.GetProperty("referencePriceMaxKwd26").GetDecimal() > 0m
            && payload.GetProperty("customerListPriceMaxKwd").GetDecimal() > 0m
            && capKind is "StatisticalSoftReview" or "HardCommercialCeiling" or "BindingLegalContractCap"
            && capBasis == "ReferencePrice26"
            && reviewMethod == "NearestRankQ3Plus1_5Iqr"
            && (contributionMode == "AmountKwdPerServiceDay" && contributionRate == 0m
                || contributionMode == "RateOfNetCustomerPrice" && contributionAmount == 0m && contributionRate <= 1m)
            && influencerTreatment is "NotApplicable" or "ApprovedContract"
            && incrementalTreatment is "NotApplicable" or "ApprovedSchedule"
            && campaignTreatment is "NotApplicable" or "ApprovedBudgetSchedule"
            && quoteLifetimeSeconds is >= 60 and <= 3600
            && gatewayPct <= 0.5m
            && rMax <= 1m
            && rMin <= rMax
            && dShort >= 1
            && dLong > dShort
            && gamma is >= 0.1m and <= 5m
            && gamma * 1000m == decimal.Truncate(gamma * 1000m)
            && payload.GetProperty("roundingStepKwd").GetDecimal() > 0m
            && payload.GetProperty("supplyCostTreatment").GetString() == "ContractedGrossSupply"
            && payload.GetProperty("contributionBasis").GetString() == "NetCustomerPrice";
    }
}
