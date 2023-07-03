using System.Runtime.CompilerServices;
using System;
using System.Linq;
using System.Collections.Generic;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Binding.Properties;
using System.Linq.Expressions;
using DotVVM.Framework.Controls;
using FastExpressionCompiler;
using DotVVM.Framework.Utils;

namespace DotVVM.Framework.Binding
{
    public static class BindingCombinator
    {
        static readonly ConditionalWeakTable<BindingCombinatorDescriptor, ConditionalWeakTable<IBinding, ConditionalWeakTable<IBinding, Lazy<IBinding>>>> combinationCache =
            new ConditionalWeakTable<BindingCombinatorDescriptor, ConditionalWeakTable<IBinding, ConditionalWeakTable<IBinding, Lazy<IBinding>>>>();

        public static IBinding GetCombination(this BindingCombinatorDescriptor descriptor, IBinding a, IBinding b)
        {
            return combinationCache
                .GetOrCreateValue(descriptor)
                .GetOrCreateValue(a)
                .GetValue(b, _ => new Lazy<IBinding>(() => descriptor.ComputeCombination(a, b)))
                .Value;
        }

        static IBinding CreateAndAlsoCombination(IBinding a, IBinding b) =>
            a.DeriveBinding(
                Expression.AndAlso(
                    a.GetProperty<ParsedExpressionBindingProperty>().Expression,
                    b.GetProperty<ParsedExpressionBindingProperty>().Expression
                )
            );
        static IBinding CreateOrElseCombination(IBinding a, IBinding b) =>
            a.DeriveBinding(
                Expression.OrElse(
                    a.GetProperty<ParsedExpressionBindingProperty>().Expression,
                    b.GetProperty<ParsedExpressionBindingProperty>().Expression
                )
            );
        public static readonly BindingCombinatorDescriptor OrElseCombination = new BindingCombinatorDescriptor(CreateOrElseCombination);
        public static readonly BindingCombinatorDescriptor AndAlsoCombination = new BindingCombinatorDescriptor(CreateAndAlsoCombination);
        public static void AndAssignProperty(this DotvvmBindableObject obj, DotvvmProperty property, object value)
        {
            if (property.PropertyType != typeof(bool)) throw new NotSupportedException($"Can only AND boolean properties, {property} is of type {property.PropertyType}");
            if (!obj.IsPropertySet(property))
            {
                obj.SetValue(property, value);
            }
            else
            {
                var currentValue = obj.GetValueRaw(property);
                if (false.Equals(currentValue) || true.Equals(value))
                {
                    // no change
                }
                else if (false.Equals(value))
                    obj.SetValue(property, false);
                else if (true.Equals(currentValue))
                    obj.SetValue(property, value);
                else
                {
                    var currentBinding = currentValue as IBinding ??
                        throw new NotSupportedException($"A IBinding instance or bool was expected in property {property}, got {obj.GetValue(property)?.GetType().ToCode() ?? "null"}");
                    var binding = value as IBinding ??
                        throw new NotSupportedException($"A IBinding instance or bool was expected to AndAssign to property {property}, got {obj.GetValue(property)?.GetType().ToCode() ?? "null"}");
                    // resource + value binding can't be combined - evaluate the resource binding
                    if (currentBinding is IValueBinding != binding is IValueBinding)
                    {
                        var valueBinding = currentBinding is IValueBinding ? currentBinding : binding;
                        var resourceBinding = currentBinding is IValueBinding ? binding : currentBinding;
                        obj.SetValue(property, (bool)obj.EvalPropertyValue(property, resourceBinding)! ? valueBinding : BoxingUtils.False);
                    }
                    else
                    {
                        obj.SetValue(property,
                            AndAlsoCombination.GetCombination(
                                currentBinding,
                                binding
                            )
                        );
                    }
                }
            }
        }

        public class BindingCombinatorDescriptor
        {
            private readonly Func<IBinding, IBinding, IBinding> func;

            public BindingCombinatorDescriptor(Func<IBinding, IBinding, IBinding> func)
            {
                this.func = func;
            }
            public IBinding ComputeCombination(IBinding a, IBinding b) => func(a, b);
        }
    }
}
