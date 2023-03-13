﻿using System;
using System.Collections.Generic;

namespace Codist.Refactorings
{
	static class All
	{
		internal static readonly IRefactoring[] Refactorings = new IRefactoring[] {
			ReplaceNode.ConcatToInterpolatedString,
			ReplaceToken.InvertOperator,
			ReplaceNode.MergeToConditional,
			ReplaceNode.WrapInElse,
			ReplaceNode.MultiLineExpression,
			ReplaceNode.MultiLineList,
			ReplaceNode.MultiLineMemberAccess,
			ReplaceNode.ConditionalToIf,
			ReplaceNode.IfToConditional,
			ReplaceNode.MergeCondition,
			ReplaceNode.SwapConditionResults,
			ReplaceNode.InlineVariable,
			ReplaceNode.While,
			ReplaceNode.AsToCast,
			ReplaceNode.SealClass,
			ReplaceNode.DuplicateMethodDeclaration,
			ReplaceNode.MakePublic,
			ReplaceNode.MakeProtected,
			ReplaceNode.MakeInternal,
			ReplaceNode.MakePrivate,
			ReplaceNode.SwapOperands,
			ReplaceNode.NestCondition,
			ReplaceNode.AddBraces,
			ReplaceNode.WrapInUsing,
			ReplaceNode.WrapInIf,
			ReplaceNode.WrapInTryCatch,
			ReplaceNode.WrapInTryFinally,
			ReplaceNode.WrapInRegion,
			ReplaceToken.UseStaticDefault,
			ReplaceToken.UseExplicitType,
			ReplaceNode.DeleteCondition,
			ReplaceNode.RemoveContainingStatement,
			ReplaceText.WrapInRegion,
			ReplaceText.WrapInIf,
		};
	}
}
