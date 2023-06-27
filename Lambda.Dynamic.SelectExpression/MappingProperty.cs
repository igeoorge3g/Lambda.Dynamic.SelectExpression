using Lambda.Dynamic.SelectExpression.Attributes;
using System.Reflection;

namespace Lambda.Dynamic.SelectExpression
{
    internal record MappingProperty(PropertyInfo TResponseProperty, PropertyInfo TEntityProperty, MappingAttribute? IsMapping = null);
}
