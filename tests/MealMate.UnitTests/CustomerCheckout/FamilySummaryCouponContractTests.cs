using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MediatR;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilySummaryCouponContractTests
{
    [Fact]
    public async Task Summary_passes_member_and_coupon_to_the_quote_query()
    {
        var sender = Substitute.For<ISender>();
        var memberId = Guid.NewGuid();
        sender.Send(Arg.Any<GetFamilyCheckoutSummaryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<FamilyCheckoutSummaryDto>(Error.Validation("family.coupon_rejected")));
        var service = new FamilySubscriptionFlowService(sender, null!, null!, null!, null!);

        await service.GetSummaryAsync(memberId, "WELCOME10", CancellationToken.None);

        await sender.Received(1).Send(Arg.Is<GetFamilyCheckoutSummaryQuery>(
            query => query.MemberId == memberId && query.PromoCode == "WELCOME10"), Arg.Any<CancellationToken>());
    }
}
