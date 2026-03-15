namespace LakskongenSim.Market
{
    /// <summary>
    /// Complete product catalog for salmon processing value chain
    /// </summary>
    public enum ProductType
    {
        // Primary products
        FreshWhole,
        HOG,              // Head-Off Gutted
        Fillet,
        FilletSkinless,
        Portions,
        Frozen,
        
        // Value-added products
        ColdSmoked,
        HotSmoked,
        Gravlax,
        Sashimi,
        
        // Byproducts
        FishOil,
        FishMeal,
        FishSkin,
        Roe,
        
        // Boutique/specialty
        SmokedPate,
        SalmonJerky,
        Canned,
        ReadyMeal
    }
}