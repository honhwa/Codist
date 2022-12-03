﻿using System.Windows;
using System.Windows.Controls;

namespace Codist.Controls
{
	public class SymbolItemTemplateSelector : DataTemplateSelector
	{
		public override DataTemplate SelectTemplate(object item, DependencyObject container) {
			var c = container as FrameworkElement;
			var i = item as SymbolItem;
			if (i is null || i.Symbol is null && i.SyntaxNode is null && i.Location is null) {
				return c.FindResource("LabelTemplate") as DataTemplate;
			}
			else {
				return c.FindResource("SymbolItemTemplate") as DataTemplate;
			}
		}
	}
}
