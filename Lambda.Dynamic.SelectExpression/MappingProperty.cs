using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    internal record MappingProperty(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty, bool IsMapping = false);
}
