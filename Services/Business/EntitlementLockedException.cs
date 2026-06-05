using System;

namespace UniMap360.Services.Business;

public class EntitlementLockedException : Exception
{
    public string FeatureCode { get; }

    public EntitlementLockedException(string featureCode) 
        : base("Vui lòng gia hạn gói cước để sử dụng tính năng này.")
    {
        FeatureCode = featureCode;
    }

    public EntitlementLockedException(string featureCode, string message) 
        : base(message)
    {
        FeatureCode = featureCode;
    }
}
