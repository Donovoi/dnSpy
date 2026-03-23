// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using dnlib.DotNet;
using dnSpy.Analyzer.Properties;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace dnSpy.Analyzer.TreeNodes {
	sealed class MethodImplementsInterfaceNode : SearchNode {
		readonly MethodDef analyzedMethod;

		public MethodImplementsInterfaceNode(MethodDef analyzedMethod) => this.analyzedMethod = analyzedMethod ?? throw new ArgumentNullException(nameof(analyzedMethod));

		protected override void Write(ITextColorWriter output, IDecompiler decompiler) =>
			output.Write(BoxedTextColor.Text, dnSpy_Analyzer_Resources.ImplementsTreeNode);

		protected override IEnumerable<AnalyzerTreeNodeData> FetchChildren(CancellationToken ct) {
			var foundMethods = new List<MethodDef>();

			foreach (var interfaceMethod in GetExplicitInterfaceMethods(ct)) {
				if (foundMethods.Any(a => CheckEquals(a, interfaceMethod)))
					continue;
				foundMethods.Add(interfaceMethod);
				yield return new MethodNode(interfaceMethod) { Context = Context };
			}

			if (analyzedMethod is { IsVirtual: false, IsStatic: false } || analyzedMethod.IsAbstract)
				yield break;

			foreach (var info in GetCandidateInterfaces(ct)) {
				ct.ThrowIfCancellationRequested();
				foreach (var interfaceMethod in info.type.Methods) {
					if (foundMethods.Any(a => CheckEquals(a, interfaceMethod)))
						continue;
					if (interfaceMethod.Name != analyzedMethod.Name)
						continue;
					bool canBeImplicitlyImplemented = !interfaceMethod.IsStatic || interfaceMethod.IsAbstract;
					if (!canBeImplicitlyImplemented)
						continue;
					if (!TypesHierarchyHelpers.MatchInterfaceMethod(interfaceMethod, analyzedMethod, info.reference))
						continue;
					foundMethods.Add(interfaceMethod);
					yield return new MethodNode(interfaceMethod) { Context = Context };
				}
			}
		}

		IEnumerable<MethodDef> GetExplicitInterfaceMethods(CancellationToken ct) {
			if (!analyzedMethod.HasOverrides)
				yield break;

			foreach (var methodOverride in analyzedMethod.Overrides) {
				ct.ThrowIfCancellationRequested();
				if (methodOverride.MethodDeclaration.ResolveMethodDef() is not MethodDef interfaceMethod)
					continue;
				if (!interfaceMethod.DeclaringType.IsInterface)
					continue;
				yield return interfaceMethod;
			}
		}

		IEnumerable<(ITypeDefOrRef reference, TypeDef type)> GetCandidateInterfaces(CancellationToken ct) {
			var checkedInterfaces = new List<ITypeDefOrRef>();
			foreach (var type in TypesHierarchyHelpers.GetTypeAndBaseTypes(analyzedMethod.DeclaringType)) {
				ct.ThrowIfCancellationRequested();
				var typeDef = type.Resolve();
				if (typeDef is null)
					continue;
				var genericArgs = type is GenericInstSig ? ((GenericInstSig)type).GenericArguments : null;
				foreach (var interfaceImpl in typeDef.Interfaces) {
					ct.ThrowIfCancellationRequested();
					var iface = GenericArgumentResolver.Resolve(interfaceImpl.Interface.ToTypeSig(), genericArgs, null)?.ToTypeDefOrRef();
					if (iface is null || checkedInterfaces.Any(a => new SigComparer().Equals(a, iface)))
						continue;
					var interfaceType = iface.ResolveTypeDef();
					if (interfaceType is null)
						continue;
					checkedInterfaces.Add(iface);
					yield return (iface, interfaceType);
				}
			}
		}

		public static bool CanShow(MethodDef method) =>
			!method.IsAbstract &&
			(method.IsVirtual || method.IsStatic) &&
			(!method.DeclaringType.IsInterface || method.HasOverrides);
	}
}
