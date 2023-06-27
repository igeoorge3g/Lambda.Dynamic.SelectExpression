namespace Lambda.Dynamic.SelectExpression.Attributes
{
    public class MappingAttribute : Attribute
    {
        public readonly string RelatedObject;
        public readonly string? RelatedProperty;

        public MappingAttribute(string relatedObject, string? relatedProperty = null)
        {
            RelatedObject = relatedObject;
            RelatedProperty = relatedProperty;
        }
    }
}
