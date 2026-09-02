using Microsoft.AspNetCore.Mvc;

namespace ViverApp.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateHoneypotAttribute : TypeFilterAttribute
{
    public ValidateHoneypotAttribute()
        : base(typeof(HoneypotActionFilter))
    {
    }
}
