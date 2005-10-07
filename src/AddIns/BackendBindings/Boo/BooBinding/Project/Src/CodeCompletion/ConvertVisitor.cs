﻿// <file>
//     <copyright see="prj:///doc/copyright.txt">2002-2005 AlphaSierraPapa</copyright>
//     <license see="prj:///doc/license.txt">GNU General Public License</license>
//     <owner name="Daniel Grunwald" email="daniel@danielgrunwald.de"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections.Generic;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Dom;
using Boo.Lang.Compiler;
using AST = Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.IO;
using Boo.Lang.Compiler.Steps;

namespace Grunwald.BooBinding.CodeCompletion
{
	public class ConvertVisitor : AbstractVisitorCompilerStep
	{
		int[] _lineLength;
		
		public ConvertVisitor(int[] _lineLength, IProjectContent pc)
		{
			this._lineLength = _lineLength;
			this._cu = new DefaultCompilationUnit(pc);
		}
		
		DefaultCompilationUnit _cu;

		public DefaultCompilationUnit Cu {
			get {
				return _cu;
			}
		}
		
		Stack<DefaultClass> _currentClass = new Stack<DefaultClass>();
		bool _firstModule = true;
		
		public override void Run()
		{
			LoggingService.Debug("RUN");
			try {
				Visit(CompileUnit);
			} catch (Exception ex) {
				MessageService.ShowError(ex);
			}
		}
		
		private ModifierEnum GetModifier(AST.TypeMember m)
		{
			ModifierEnum r = ModifierEnum.None;
			if (m.IsPublic)    r |= ModifierEnum.Public;
			if (m.IsProtected) r |= ModifierEnum.Protected;
			if (m.IsPrivate)   r |= ModifierEnum.Private;
			if (m.IsInternal)  r |= ModifierEnum.Internal;
			
			if (m.IsStatic) r |= ModifierEnum.Static;
			if (m is AST.Field) {
				if (m.IsFinal)  r |= ModifierEnum.Readonly;
			} else {
				if (!m.IsFinal) r |= ModifierEnum.Virtual;
			}
			if (m.IsAbstract) r |= ModifierEnum.Abstract;
			if (m.IsOverride) r |= ModifierEnum.Override;
			return r;
		}
		
		private int GetLineEnd(int line)
		{
			if (_lineLength == null || line < 1 || line > _lineLength.Length)
				return 0;
			else
				return _lineLength[line - 1] + 1;
		}
		
		private DomRegion GetRegion(AST.Node m)
		{
			AST.LexicalInfo l = m.LexicalInfo;
			if (l.Line < 0)
				return DomRegion.Empty;
			else
				return new DomRegion(l.Line, 0 /*l.Column*/, l.Line, GetLineEnd(l.Line));
		}
		
		private DomRegion GetClientRegion(AST.Node m)
		{
			AST.LexicalInfo l = m.LexicalInfo;
			if (l.Line < 0)
				return DomRegion.Empty;
			AST.SourceLocation l2;
			if (m is AST.Method) {
				l2 = ((AST.Method)m).Body.EndSourceLocation;
			} else if (m is AST.Property) {
				AST.Property p = (AST.Property)m;
				if (p.Getter != null && p.Getter.Body != null) {
					l2 = p.Getter.Body.EndSourceLocation;
					if (p.Setter != null && p.Setter.Body != null) {
						if (p.Setter.Body.EndSourceLocation.Line > l2.Line)
							l2 = p.Setter.Body.EndSourceLocation;
					}
				} else if (p.Setter != null && p.Setter.Body != null) {
					l2 = p.Setter.Body.EndSourceLocation;
				} else {
					l2 = p.EndSourceLocation;
				}
			} else {
				l2 = m.EndSourceLocation;
			}
			if (l2 == null || l2.Line < 0 || l.Line == l2.Line)
				return DomRegion.Empty;
			// TODO: use l.Column / l2.Column when the tab-bug has been fixed
			return new DomRegion(l.Line, GetLineEnd(l.Line), l2.Line, GetLineEnd(l2.Line));
		}
		
		public override void OnImport(AST.Import p)
		{
			DefaultUsing u = new DefaultUsing(_cu.ProjectContent);
			if (p.Alias == null)
				u.Usings.Add(p.Namespace);
			else
				u.Aliases[p.Alias.Name] = new GetClassReturnType(_cu.ProjectContent, p.Namespace, 0);
			_cu.Usings.Add(u);
		}
		
		private IClass OuterClass {
			get {
				if (_currentClass.Count > 0)
					return _currentClass.Peek();
				else
					return null;
			}
		}
		
		void ConvertTemplates(AST.Node node, DefaultClass c)
		{
			c.TypeParameters = DefaultTypeParameter.EmptyTypeParameterList;
		}
		
		void ConvertTemplates(AST.Node node, DefaultMethod m)
		{
			m.TypeParameters = DefaultTypeParameter.EmptyTypeParameterList;
		}
		
		void ConvertAttributes(AST.Node node, AbstractDecoration c)
		{
			c.Attributes = DefaultAttribute.EmptyAttributeList;
			c.Documentation = node.Documentation;
		}
		
		void ConvertParameters(AST.ParameterDeclarationCollection parameters, DefaultMethod m)
		{
			if (parameters == null || parameters.Count == 0) {
				m.Parameters = DefaultParameter.EmptyParameterList;
			} else {
				AddParameters(parameters, m.Parameters);
			}
		}
		void ConvertParameters(AST.ParameterDeclarationCollection parameters, DefaultProperty p)
		{
			if (parameters == null || parameters.Count == 0) {
				p.Parameters = DefaultParameter.EmptyParameterList;
			} else {
				AddParameters(parameters, p.Parameters);
			}
		}
		void AddParameters(AST.ParameterDeclarationCollection parameters, IList<IParameter> output)
		{
			DefaultParameter p = null;
			foreach (AST.ParameterDeclaration par in parameters) {
				p = new DefaultParameter(par.Name, CreateReturnType(par.Type), GetRegion(par));
				if (par.IsByRef) p.Modifiers |= ParameterModifiers.Ref;
				output.Add(p);
			}
			if (parameters.VariableNumber) {
				p.Modifiers |= ParameterModifiers.Params;
			}
		}
		
		IReturnType CreateReturnType(AST.TypeReference reference, IMethod method)
		{
			IClass c = OuterClass;
			if (c == null) {
				return CreateReturnType(reference, new DefaultClass(_cu, "___DummyClass"), method, 1, 1, _cu.ProjectContent, true);
			} else {
				return CreateReturnType(reference, c, method, c.Region.BeginLine + 1, 1, _cu.ProjectContent, true);
			}
		}
		public static IReturnType CreateReturnType(AST.TypeReference reference, IClass callingClass,
		                                           IMember callingMember, int caretLine, int caretColumn,
		                                           IProjectContent projectContent,
		                                           bool useLazyReturnType)
		{
			if (reference == null) {
				LoggingService.Warn("inferred return type!");
				return ReflectionReturnType.Object;
			}
			if (reference is AST.ArrayTypeReference) {
				AST.ArrayTypeReference arr = (AST.ArrayTypeReference)reference;
				return new ArrayReturnType(CreateReturnType(arr.ElementType, callingClass, callingMember,
				                                            caretLine, caretColumn, projectContent, useLazyReturnType),
				                           (int)arr.Rank.Value);
			} else if (reference is AST.SimpleTypeReference) {
				string name = ((AST.SimpleTypeReference)reference).Name;
				if (BooAmbience.ReverseTypeConversionTable.ContainsKey(name))
					return new GetClassReturnType(projectContent, BooAmbience.ReverseTypeConversionTable[name], 0);
				return new SearchClassReturnType(projectContent, callingClass, caretLine, caretColumn,
				                                 name, 0);
			} else if (reference is AST.CallableTypeReference) {
				return new AnonymousMethodReturnType();
			} else {
				throw new NotSupportedException("unknown reference type: " + reference.ToString());
			}
		}
		IReturnType CreateReturnType(AST.TypeReference reference)
		{
			return CreateReturnType(reference, null);
		}
		IReturnType CreateReturnType(Type type)
		{
			return ReflectionReturnType.CreatePrimitive(type);
		}
		// TODO: Type inference
		IReturnType CreateReturnType(AST.Field field)
		{
			return CreateReturnType(field.Type);
		}
		IReturnType CreateReturnType(AST.Method node, IMethod method)
		{
			return CreateReturnType(node.ReturnType, method);
		}
		IReturnType CreateReturnType(AST.Property property)
		{
			return CreateReturnType(property.Type);
		}
		
		public override void OnCallableDefinition(AST.CallableDefinition node)
		{
			LoggingService.Debug("OnCallableDefinition: " + node.FullName);
			DomRegion region = GetRegion(node);
			DefaultClass c = new DefaultClass(_cu, ClassType.Delegate, GetModifier(node), region, OuterClass);
			ConvertAttributes(node, c);
			c.BaseTypes.Add(ReflectionReturnType.CreatePrimitive(typeof(Delegate)));
			c.FullyQualifiedName = node.FullName;
			if (_currentClass.Count > 0) {
				OuterClass.InnerClasses.Add(c);
			} else {
				_cu.Classes.Add(c);
			}
			_currentClass.Push(c); // necessary for CreateReturnType
			ConvertTemplates(node, c);
			IReturnType returnType = CreateReturnType(node.ReturnType);
			DefaultMethod invokeMethod = new DefaultMethod("Invoke", returnType, ModifierEnum.Public, DomRegion.Empty, DomRegion.Empty, c);
			ConvertParameters(node.Parameters, invokeMethod);
			c.Methods.Add(invokeMethod);
			invokeMethod = new DefaultMethod("BeginInvoke", CreateReturnType(typeof(IAsyncResult)), ModifierEnum.Public, DomRegion.Empty, DomRegion.Empty, c);
			ConvertParameters(node.Parameters, invokeMethod);
			invokeMethod.Parameters.Add(new DefaultParameter("callback", CreateReturnType(typeof(AsyncCallback)), DomRegion.Empty));
			invokeMethod.Parameters.Add(new DefaultParameter("object", ReflectionReturnType.Object, DomRegion.Empty));
			c.Methods.Add(invokeMethod);
			invokeMethod = new DefaultMethod("EndInvoke", returnType, ModifierEnum.Public, DomRegion.Empty, DomRegion.Empty, c);
			invokeMethod.Parameters.Add(new DefaultParameter("result", CreateReturnType(typeof(IAsyncResult)), DomRegion.Empty));
			c.Methods.Add(invokeMethod);
			_currentClass.Pop();
		}
		
		public override bool EnterClassDefinition(AST.ClassDefinition node)
		{
			EnterTypeDefinition(node, ClassType.Class);
			return base.EnterClassDefinition(node);
		}
		
		public override bool EnterInterfaceDefinition(AST.InterfaceDefinition node)
		{
			EnterTypeDefinition(node, ClassType.Interface);
			return base.EnterInterfaceDefinition(node);
		}
		
		public override bool EnterEnumDefinition(AST.EnumDefinition node)
		{
			EnterTypeDefinition(node, ClassType.Enum);
			return base.EnterEnumDefinition(node);
		}
		
		public override bool EnterModule(AST.Module node)
		{
			if (_firstModule) EnterTypeDefinition(node, ClassType.Class);
			_firstModule = false;
			return base.EnterModule(node);
		}
		
		private void EnterTypeDefinition(AST.TypeDefinition node, ClassType classType)
		{
			LoggingService.Debug("Enter " + node.GetType().Name + " (" + node.FullName + ")");
			DomRegion region = GetClientRegion(node);
			DefaultClass c = new DefaultClass(_cu, classType, GetModifier(node), region, OuterClass);
			c.FullyQualifiedName = node.FullName;
			if (_currentClass.Count > 0)
				_currentClass.Peek().InnerClasses.Add(c);
			else
				_cu.Classes.Add(c);
			_currentClass.Push(c);
			ConvertAttributes(node, c);
			ConvertTemplates(node, c);
			if (node.BaseTypes != null) {
				foreach (AST.TypeReference r in node.BaseTypes) {
					c.BaseTypes.Add(CreateReturnType(r));
				}
			}
		}
		
		public override void LeaveClassDefinition(AST.ClassDefinition node)
		{
			LeaveTypeDefinition(node);
			base.LeaveClassDefinition(node);
		}
		
		public override void LeaveInterfaceDefinition(AST.InterfaceDefinition node)
		{
			LeaveTypeDefinition(node);
			base.LeaveInterfaceDefinition(node);
		}
		
		public override void LeaveEnumDefinition(AST.EnumDefinition node)
		{
			LeaveTypeDefinition(node);
			base.LeaveEnumDefinition(node);
		}
		
		public override void LeaveModule(AST.Module node)
		{
			if (_currentClass.Count != 0) LeaveTypeDefinition(node);
			base.LeaveModule(node);
		}
		
		private void LeaveTypeDefinition(AST.TypeDefinition node)
		{
			DefaultClass c = _currentClass.Pop();
			LoggingService.Debug("Leave "+node.GetType().Name+" "+node.FullName+" (Class = "+c.FullyQualifiedName+")");
		}
		
		public override void OnMethod(AST.Method node)
		{
			LoggingService.Debug("Method: " + node.FullName);
			DefaultMethod method = new DefaultMethod(node.Name, null, GetModifier(node), GetRegion(node), GetClientRegion(node), OuterClass);
			ConvertAttributes(node, method);
			ConvertTemplates(node, method);
			// return type must be assign AFTER ConvertTemplates
			method.ReturnType = CreateReturnType(node, method);
			ConvertParameters(node.Parameters, method);
			_currentClass.Peek().Methods.Add(method);
		}
		
		public override void OnConstructor(AST.Constructor node)
		{
			if (node.Body.Statements.Count == 0) return;
			Constructor ctor = new Constructor(GetModifier(node), GetRegion(node), GetClientRegion(node), OuterClass);
			ConvertAttributes(node, ctor);
			ConvertParameters(node.Parameters, ctor);
			_currentClass.Peek().Methods.Add(ctor);
		}
		
		public override void OnEnumMember(AST.EnumMember node)
		{
			DefaultField field = new DefaultField(OuterClass.DefaultReturnType, node.Name, ModifierEnum.Const | ModifierEnum.Public, GetRegion(node), OuterClass);
			ConvertAttributes(node, field);
			OuterClass.Fields.Add(field);
		}
		
		public override void OnField(AST.Field node)
		{
			DefaultField field = new DefaultField(CreateReturnType(node), node.Name, GetModifier(node), GetRegion(node), OuterClass);
			ConvertAttributes(node, field);
			OuterClass.Fields.Add(field);
		}
		
		public override void OnEvent(AST.Event node)
		{
			DomRegion region = GetRegion(node);
			DefaultEvent e = new DefaultEvent(node.Name, CreateReturnType(node.Type), GetModifier(node), region, region, OuterClass);
			ConvertAttributes(node, e);
			OuterClass.Events.Add(e);
		}
		
		public override void OnProperty(AST.Property node)
		{
			DefaultProperty property = new DefaultProperty(node.Name, CreateReturnType(node), GetModifier(node), GetRegion(node), GetClientRegion(node), OuterClass);
			ConvertAttributes(node, property);
			ConvertParameters(node.Parameters, property);
			OuterClass.Properties.Add(property);
		}
	}
}
