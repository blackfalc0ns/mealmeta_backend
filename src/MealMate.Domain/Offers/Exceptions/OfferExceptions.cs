using MealMate.Domain.Exceptions;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Domain.Offers.Exceptions;

public sealed class InvalidOfferStateTransitionException : DomainException
{
    public InvalidOfferStateTransitionException(OfferStatus from, OfferStatus to)
        : base("Offer.InvalidStateTransition", $"Cannot transition offer from status '{from}' to '{to}'.")
    {
    }
}

public sealed class OfferEligibilityException : DomainException
{
    public OfferEligibilityException(string code, string message)
        : base(code, message)
    {
    }
}
