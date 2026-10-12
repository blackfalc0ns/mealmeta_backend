using System.Globalization;
using MealMate.Api.Resources;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class FamilyCheckoutMessageResourceTests
{
    [Theory]
    [InlineData("family.permission.viewCalendar")]
    [InlineData("family.permission.viewFamilyCalendars")]
    [InlineData("family.permission.viewProgram")]
    [InlineData("family.permission.viewFamilyPlans")]
    [InlineData("family.permission.viewInvoices")]
    [InlineData("family.permission.viewFamilyInvoices")]
    [InlineData("family.permission.selectRestaurant")]
    [InlineData("family.permission.confirmMeals")]
    [InlineData("family.permission.requestFreeze")]
    [InlineData("family.permission.requestRenewal")]
    [InlineData("family.permission.requestUpgrade")]
    [InlineData("family.permission.cancelSubscription")]
    [InlineData("family.permission_group.account")]
    [InlineData("family.checkout_no_selection")]
    [InlineData("family.quote_changed")]
    [InlineData("family.batch_review_required")]
    [InlineData("family.payment_initializing")]
    [InlineData("family.idempotency_key_used")]
    [InlineData("family.batch_closed")]
    [InlineData("family.batch_unresolved")]
    [InlineData("family.batch_not_payable")]
    [InlineData("family.batch_targets_invalid")]
    [InlineData("family.batch_amount_mismatch")]
    [InlineData("family.batch_currency_mismatch")]
    [InlineData("family.renewal_configuration_invalid")]
    [InlineData("family.renewal_intent_changed")]
    [InlineData("family.renewal_member_changed")]
    [InlineData("family.renewal_not_available")]
    [InlineData("family.renewal_dates_overlap")]
    [InlineData("family.renewal_pointer_changed")]
    [InlineData("family.renewal_quote_invalid")]
    [InlineData("family.renewal_idempotency_mismatch")]
    [InlineData("family.member_live_service_requires_settlement")]
    [InlineData("family.invoice_creation_stopped")]
    [InlineData("family.client_update_required")]
    [InlineData("family.payment_method_unavailable")]
    [InlineData("family.payment_currency_unsupported")]
    [InlineData("family.currency_mismatch")]
    [InlineData("family.actual_checkout_required")]
    public void Messages_follow_ar_en_ar_request_culture(string key)
    {
        var manager = new System.Resources.ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);
        var arabic = manager.GetString(key, CultureInfo.GetCultureInfo("ar"));
        var english = manager.GetString(key, CultureInfo.GetCultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(arabic));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.NotEqual(arabic, english);
        Assert.NotEqual(key, english);
        Assert.Equal(arabic, manager.GetString(key, CultureInfo.GetCultureInfo("ar")));
    }
}
