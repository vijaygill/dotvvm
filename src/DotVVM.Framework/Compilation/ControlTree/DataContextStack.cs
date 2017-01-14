﻿using System;
using System.Collections.Generic;
using DotVVM.Framework.Compilation.ControlTree.Resolved;

namespace DotVVM.Framework.Compilation.ControlTree
{
    public class DataContextStack : IDataContextStack
    {
        public DataContextStack Parent { get; }
        public Type DataContextType { get; }
        public Type RootControlType { get; }
        public IReadOnlyList<NamespaceImport> NamespaceImports { get; }
        public int DataContextSpaceId { get; }

        public DataContextStack(Type type,
            DataContextStack parent = null,
            Type rootControlType = null,
            IReadOnlyList<NamespaceImport> imports = null,
            int contextId = -1)
        {
            Parent = parent;
            DataContextType = type;
            RootControlType = rootControlType ?? parent?.RootControlType;
            NamespaceImports = imports ?? parent?.NamespaceImports;
            DataContextSpaceId = contextId > 0 ? contextId : AssignId();
        }

        public IEnumerable<Type> Enumerable()
        {
            var c = this;
            while (c != null)
            {
                yield return c.DataContextType;
                c = c.Parent;
            }
        }

        public IEnumerable<Type> Parents()
        {
            var c = Parent;
            while (c != null)
            {
                yield return c.DataContextType;
                c = c.Parent;
            }
        }

        ITypeDescriptor IDataContextStack.DataContextType => new ResolvedTypeDescriptor(DataContextType);
        IDataContextStack IDataContextStack.Parent => Parent;

        private static int _idCounter;
        public static int AssignId() => System.Threading.Interlocked.Add(ref _idCounter, 1);

    }
}
