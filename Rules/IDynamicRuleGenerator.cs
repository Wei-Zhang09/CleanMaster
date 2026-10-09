using CleanMaster.Models;

namespace CleanMaster.Rules;

public interface IDynamicRuleGenerator
{
    List<CleanupRule> Generate();
}
