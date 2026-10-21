using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

public static class RuleVersionScope
{
    public const string GlobalScopeId = "global";

    public static string Normalize(RuleScopeType scopeType, Guid? scopeEntityId)
    {
        if (scopeType == RuleScopeType.Global)
        {
            return GlobalScopeId;
        }

        return scopeEntityId is { } scopedEntityId
            ? scopedEntityId.ToString("N")
            : throw new ArgumentException("A scoped rule requires a scope entity id.", nameof(scopeEntityId));
    }
}
