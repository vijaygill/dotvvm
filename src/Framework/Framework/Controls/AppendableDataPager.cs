using System;
using System.Collections.Generic;
using System.Text;
using DotVVM.Framework.Binding;
using DotVVM.Framework.Binding.Expressions;
using DotVVM.Framework.Binding.HelperNamespace;
using DotVVM.Framework.Compilation.Javascript;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Utils;

namespace DotVVM.Framework.Controls
{
    /// <summary>
    /// Renders a pager for <see cref="GridViewDataSet{T}"/> that allows the user to append more items to the end of the list.
    /// </summary>
    [ControlMarkupOptions(AllowContent = false, DefaultContentProperty = nameof(LoadTemplate))]
    public class AppendableDataPager : HtmlGenericControl 
    {
        private readonly GridViewDataSetBindingProvider gridViewDataSetBindingProvider;
        private readonly BindingCompilationService bindingService;

        [MarkupOptions(AllowBinding = false, MappingMode = MappingMode.InnerElement)]
        [DataPagerApi.AddParameterDataContextChange("_dataPager")]
        public ITemplate? LoadTemplate
        {
            get { return (ITemplate?)GetValue(LoadTemplateProperty); }
            set { SetValue(LoadTemplateProperty, value); }
        }
        public static readonly DotvvmProperty LoadTemplateProperty
            = DotvvmProperty.Register<ITemplate, AppendableDataPager>(c => c.LoadTemplate, null);

        [MarkupOptions(AllowBinding = false, MappingMode = MappingMode.InnerElement)]
        [DataPagerApi.AddParameterDataContextChange("_dataPager")]
        public ITemplate? LoadingTemplate
        {
            get { return (ITemplate?)GetValue(LoadingTemplateProperty); }
            set { SetValue(LoadingTemplateProperty, value); }
        }
        public static readonly DotvvmProperty LoadingTemplateProperty
            = DotvvmProperty.Register<ITemplate?, AppendableDataPager>(c => c.LoadingTemplate, null);

        [MarkupOptions(AllowBinding = false, MappingMode = MappingMode.InnerElement)]
        public ITemplate? EndTemplate
        {
            get { return (ITemplate?)GetValue(EndTemplateProperty); }
            set { SetValue(EndTemplateProperty, value); }
        }
        public static readonly DotvvmProperty EndTemplateProperty
            = DotvvmProperty.Register<ITemplate, AppendableDataPager>(c => c.EndTemplate, null);

        [MarkupOptions(Required = true, AllowHardCodedValue = false)]
        public IPageableGridViewDataSet DataSet
        {
            get { return (IPageableGridViewDataSet)GetValue(DataSetProperty)!; }
            set { SetValue(DataSetProperty, value); }
        }
        public static readonly DotvvmProperty DataSetProperty
            = DotvvmProperty.Register<IPageableGridViewDataSet, AppendableDataPager>(c => c.DataSet, null);

        [MarkupOptions(Required = true)]
        public ICommandBinding? LoadData
        {
            get => (ICommandBinding?)GetValue(LoadDataProperty);
            set => SetValue(LoadDataProperty, value);
        }
        public static readonly DotvvmProperty LoadDataProperty =
            DotvvmProperty.Register<ICommandBinding?, AppendableDataPager>(nameof(LoadData));


        private DataPagerBindings? dataPagerCommands = null;


        public AppendableDataPager(GridViewDataSetBindingProvider gridViewDataSetBindingProvider, BindingCompilationService bindingService) : base("div")
        {
            this.gridViewDataSetBindingProvider = gridViewDataSetBindingProvider;
            this.bindingService = bindingService;
        }

        protected internal override void OnLoad(IDotvvmRequestContext context)
        {
            var dataSetBinding = GetValueBinding(DataSetProperty)!;
            var commandType = LoadData is { } ? GridViewDataSetCommandType.LoadDataDelegate : GridViewDataSetCommandType.Default;
            dataPagerCommands = gridViewDataSetBindingProvider.GetDataPagerCommands(this.GetDataContextType()!, dataSetBinding, commandType);

            if (LoadTemplate != null)
            {
                var templateDataContext = LoadTemplateProperty.GetDataContextType(this)!;
                var container = new PlaceHolder()
                    .SetProperty(p => p.IncludeInPage, bindingService.Cache.CreateValueBinding("_dataPager.CanLoadNextPage", templateDataContext));
                container.SetDataContextType(templateDataContext);
                Children.Add(container);

                LoadTemplate.BuildContent(context, container);
            }

            if (LoadingTemplate != null)
            {
                var templateDataContext = LoadingTemplateProperty.GetDataContextType(this)!;
                var container = new PlaceHolder()
                    .SetProperty(p => p.IncludeInPage, bindingService.Cache.CreateValueBinding("_dataPager.IsLoading", templateDataContext));
                container.SetDataContextType(templateDataContext);
                Children.Add(container);

                LoadingTemplate.BuildContent(context, container);
            }

            if (EndTemplate != null)
            {
                var container = new PlaceHolder()
                    .SetProperty(p => p.IncludeInPage, dataPagerCommands.IsLastPage);
                Children.Add(container);

                EndTemplate.BuildContent(context, container);
            }
        }

        protected override void AddAttributesToRender(IHtmlWriter writer, IDotvvmRequestContext context)
        {
            var dataSetBinding = GetDataSetBinding().GetKnockoutBindingExpression(this, unwrapped: true);

            var loadData = this.LoadData.NotNull("AppendableDataPager.LoadData is currently required.");
            var loadDataCore = KnockoutHelper.GenerateClientPostbackLambda("LoadDataCore", loadData, this, PostbackScriptOptions.KnockoutBinding with { AllowPostbackHandlers = false });
            var loadNextPage = KnockoutHelper.GenerateClientPostbackLambda("LoadData", dataPagerCommands!.GoToNextPage!, this, PostbackScriptOptions.KnockoutBinding with {
                ParameterAssignment = p =>
                    p == GridViewDataSetBindingProvider.LoadDataDelegate ? new CodeParameterAssignment(loadDataCore, default) :
                    p == GridViewDataSetBindingProvider.PostProcessorDelegate ? new CodeParameterAssignment("dotvvm.dataSet.postProcessors.append", OperatorPrecedence.Max) :
                    default
            });
            
            var binding = new KnockoutBindingGroup();
            binding.Add("dataSet", dataSetBinding);
            binding.Add("loadNextPage", loadNextPage);
            binding.Add("autoLoadWhenInViewport", LoadTemplate is null ? "true" : "false");
            writer.AddKnockoutDataBind("dotvvm-appendable-data-pager", binding);
            
            base.AddAttributesToRender(writer, context);
        }

        private IValueBinding GetDataSetBinding()
            => GetValueBinding(DataSetProperty) ?? throw new DotvvmControlException(this, "The DataSet property of the dot:DataPager control must be set!");
    }
}
