namespace RelicDealFinder.Enums;

public enum UserStatus
{
    Offline,
    //Invisible, --  is exposed as "Offline" in UserShort model which we use here: https://docs.warframe.market/docs/data-models#usershort
    Online,
    InGame
}