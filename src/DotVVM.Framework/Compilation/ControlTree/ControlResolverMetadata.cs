using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Runtime;
using Newtonsoft.Json;

namespace DotVVM.Framework.Compilation.ControlTree
{
    public class ControlResolverMetadata : ControlResolverMetadataBase
    {
        private readonly ControlType controlType;

        public new Type Type => controlType.Type;

        [JsonIgnore]
        public Type ControlBuilderType => controlType.ControlBuilderType;

        [JsonIgnore]
        public new DotvvmProperty DefaultContentProperty => (DotvvmProperty) base.DefaultContentProperty;

        [JsonIgnore]
        public new Type DataContextConstraint => controlType.DataContextRequirement;


        public ControlResolverMetadata(ControlType controlType) : base(controlType)
        {
            this.controlType = controlType;

            DataContextChangeAttributes = Type.GetCustomAttributes<DataContextChangeAttribute>(true).ToArray();
        }

        public ControlResolverMetadata(Type type) : base(new ControlType(type))
        {
        }

        [JsonIgnore]
        public override DataContextChangeAttribute[] DataContextChangeAttributes { get; }

        protected override void LoadProperties(Dictionary<string, IPropertyDescriptor> result)
        {
            foreach (var property in DotvvmProperty.ResolveProperties(controlType.Type).Concat(DotvvmProperty.GetVirtualProperties(controlType.Type)))
            {
                result.Add(property.Name, property);
            }
        }
        
        /// <summary>
        /// Finds the property.
        /// </summary>
        public DotvvmProperty FindProperty(string name)
        {
            IPropertyDescriptor result;
            return Properties.TryGetValue(name, out result) ? (DotvvmProperty)result : null;
        }

    }
}