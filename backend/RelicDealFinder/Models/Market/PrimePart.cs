namespace RelicDealFinder.Models.Market;

/* This model is only needed for the EFCore table name.
 * MarketItem model describes all the potential items returned by the WFM api call,
 * but we only need to persist the Prime parts for our use-case.
 */
public class PrimePart : MarketItem { }
