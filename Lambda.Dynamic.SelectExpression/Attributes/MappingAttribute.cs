namespace Lambda.Dynamic.SelectExpression.Attributes
{
    public class MappingAttribute : Attribute
    {
        public readonly string RelatedObject;
        public MappingAttribute(string relatedObject)
        {
            RelatedObject = relatedObject;
        }
    }
}
