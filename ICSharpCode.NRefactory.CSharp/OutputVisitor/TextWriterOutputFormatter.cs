﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace ICSharpCode.NRefactory.CSharp {
	/// <summary>
	/// Writes C# code into a TextWriter.
	/// </summary>
	public class TextWriterTokenWriter : TokenWriter, ILocatable
	{
		readonly TextWriter textWriter;
		int indentation;
		bool needsIndent = true;
		bool isAtStartOfLine = true;
		int line, column;

		public int Indentation {
			get { return this.indentation; }
			set { this.indentation = value; }
		}
		
		public TextLocation Location {
			get { return new TextLocation(line, column + (needsIndent ? indentation * IndentationString.Length : 0)); }
		}
		
		public string IndentationString { get; set; }
		
		public TextWriterTokenWriter(TextWriter textWriter)
		{
			if (textWriter == null)
				throw new ArgumentNullException("textWriter");
			this.textWriter = textWriter;
			this.IndentationString = "\t";
			this.line = 1;
			this.column = 1;
		}
		
		public override void WriteIdentifier(Identifier identifier, object data)
		{
			WriteIndentation();
			if (!BoxedTextColor.Keyword.Equals(data) && (identifier.IsVerbatim || CSharpOutputVisitor.IsKeyword(identifier.Name, identifier))) {
				textWriter.Write('@');
				column++;
			}
			textWriter.Write(identifier.Name);
			column += identifier.Name.Length;
			isAtStartOfLine = false;
		}
		
		public override void WriteKeyword(Role role, string keyword)
		{
			WriteIndentation();
			column += keyword.Length;
			textWriter.Write(keyword);
			isAtStartOfLine = false;
		}
		
		public override void WriteToken(Role role, string token, object data)
		{
			WriteIndentation();
			column += token.Length;
			textWriter.Write(token);
			isAtStartOfLine = false;
		}
		
		public override void Space()
		{
			WriteIndentation();
			column++;
			textWriter.Write(' ');
		}
		
		protected void WriteIndentation()
		{
			if (needsIndent) {
				needsIndent = false;
				for (int i = 0; i < indentation; i++) {
					textWriter.Write(this.IndentationString);
				}
				column += indentation * IndentationString.Length;
			}
		}
		
		public override void NewLine()
		{
			textWriter.WriteLine();
			column = 1;
			line++;
			needsIndent = true;
			isAtStartOfLine = true;
		}
		
		public override void Indent()
		{
			indentation++;
		}
		
		public override void Unindent()
		{
			indentation--;
		}
		
		public override void WriteComment(CommentType commentType, string content, CommentReference[] refs)
		{
			WriteIndentation();
			switch (commentType) {
				case CommentType.SingleLine:
					textWriter.Write("//");
					textWriter.WriteLine(content);
					column += 2 + content.Length;
					needsIndent = true;
					isAtStartOfLine = true;
					break;
				case CommentType.MultiLine:
					textWriter.Write("/*");
					textWriter.Write(content);
					textWriter.Write("*/");
					column += 2;
					UpdateEndLocation(content, ref line, ref column);
					column += 2;
					isAtStartOfLine = false;
					break;
				case CommentType.Documentation:
					textWriter.Write("///");
					textWriter.WriteLine(content);
					column += 3 + content.Length;
					needsIndent = true;
					isAtStartOfLine = true;
					break;
				case CommentType.MultiLineDocumentation:
					textWriter.Write("/**");
					textWriter.Write(content);
					textWriter.Write("*/");
					column += 3;
					UpdateEndLocation(content, ref line, ref column);
					column += 2;
					isAtStartOfLine = false;
					break;
				default:
					textWriter.Write(content);
					column += content.Length;
					break;
			}
		}
		
		static void UpdateEndLocation(string content, ref int line, ref int column)
		{
			if (string.IsNullOrEmpty(content))
				return;
			for (int i = 0; i < content.Length; i++) {
				char ch = content[i];
				switch (ch) {
					case '\r':
						if (i + 1 < content.Length && content[i + 1] == '\n')
							i++;
						goto case '\n';
					case '\n':
						line++;
						column = 0;
						break;
				}
				column++;
			}
		}
		
		public override void WritePreProcessorDirective(PreProcessorDirectiveType type, string argument)
		{
			// pre-processor directive must start on its own line
			if (!isAtStartOfLine)
				NewLine();
			WriteIndentation();
			textWriter.Write('#');
			string directive = type.ToString().ToLowerInvariant();
			textWriter.Write(directive);
			column += 1 + directive.Length;
			if (!string.IsNullOrEmpty(argument)) {
				textWriter.Write(' ');
				textWriter.Write(argument);
				column += 1 + argument.Length;
			}
			NewLine();
		}
		
		public static string PrintPrimitiveValue(object value)
		{
			TextWriter writer = new StringWriter();
			TextWriterTokenWriter tokenWriter = new TextWriterTokenWriter(writer);
			tokenWriter.WritePrimitiveValue(value, CSharpMetadataTextColorProvider.Instance.GetColor(value));
			return writer.ToString();
		}
		
		public override void WritePrimitiveValue(object value, object data = null, string literalValue = null)
		{
			WritePrimitiveValue(value, data, literalValue, ref column, (a, b) => textWriter.Write(a), WriteToken);
		}
		
		public static void WritePrimitiveValue(object value, object data, string literalValue, ref int column, Action<string, object> writer, Action<Role, string, object> writeToken)
		{
			if (literalValue != null) {
				Debug.Assert(data != null);
				writer(literalValue, data ?? BoxedTextColor.Text);
				column += literalValue.Length;
				return;
			}
			
			if (value == null) {
				// usually NullReferenceExpression should be used for this, but we'll handle it anyways
				writer("null", BoxedTextColor.Keyword);
				column += 4;
				return;
			}
			
			if (value is bool) {
				if ((bool)value) {
					writer("true", BoxedTextColor.Keyword);
					column += 4;
				} else {
					writer("false", BoxedTextColor.Keyword);
					column += 5;
				}
				return;
			}

			var s = value as string;
			if (s != null) {
				string tmp = "\"" + ConvertString(s) + "\"";
				column += tmp.Length;
				writer(tmp, BoxedTextColor.String);
			} else if (value is char) {
				string tmp = "'" + ConvertCharLiteral((char)value) + "'";
				column += tmp.Length;
				writer(tmp, BoxedTextColor.Char);
			} else if (value is decimal) {
				string str = ((decimal)value).ToString(NumberFormatInfo.InvariantInfo) + "m";
				column += str.Length;
				writer(str, BoxedTextColor.Number);
			} else if (value is float) {
				float f = (float)value;
				if (float.IsInfinity(f) || float.IsNaN(f)) {
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					writer("float", BoxedTextColor.Keyword);
					column += 5;
					writeToken(Roles.Dot, ".", BoxedTextColor.Operator);
					if (float.IsPositiveInfinity(f)) {
						writer("PositiveInfinity", BoxedTextColor.LiteralField);
						column += "PositiveInfinity".Length;
					} else if (float.IsNegativeInfinity(f)) {
						writer("NegativeInfinity", BoxedTextColor.LiteralField);
						column += "NegativeInfinity".Length;
					} else {
						writer("NaN", BoxedTextColor.LiteralField);
						column += 3;
					}
					return;
				}
				var number = f.ToString("R", NumberFormatInfo.InvariantInfo) + "f";
				if (f == 0 && 1 / f == float.NegativeInfinity) {
					// negative zero is a special case
					// (again, not a primitive expression, but it's better to handle
					// the special case here than to do it in all code generators)
					number = "-" + number;
				}
				column += number.Length;
				writer(number, BoxedTextColor.Number);
			} else if (value is double) {
				double f = (double)value;
				if (double.IsInfinity(f) || double.IsNaN(f)) {
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					writer("double", BoxedTextColor.Keyword);
					column += 6;
					writeToken(Roles.Dot, ".", BoxedTextColor.Operator);
					if (double.IsPositiveInfinity(f)) {
						writer("PositiveInfinity", BoxedTextColor.LiteralField);
						column += "PositiveInfinity".Length;
					} else if (double.IsNegativeInfinity(f)) {
						writer("NegativeInfinity", BoxedTextColor.LiteralField);
						column += "NegativeInfinity".Length;
					} else {
						writer("NaN", BoxedTextColor.LiteralField);
						column += 3;
					}
					return;
				}
				string number = f.ToString("R", NumberFormatInfo.InvariantInfo);
				if (f == 0 && 1 / f == double.NegativeInfinity) {
					// negative zero is a special case
					// (again, not a primitive expression, but it's better to handle
					// the special case here than to do it in all code generators)
					number = "-" + number;
				}
				if (number.IndexOf('.') < 0 && number.IndexOf('E') < 0) {
					number += ".0";
				}
				column += number.Length;
				writer(number, BoxedTextColor.Number);
			} else if (value is IFormattable) {
				StringBuilder b = new StringBuilder ();
//				if (primitiveExpression.LiteralFormat == LiteralFormat.HexadecimalNumber) {
//					b.Append("0x");
//					b.Append(((IFormattable)val).ToString("x", NumberFormatInfo.InvariantInfo));
//				} else {
					b.Append(((IFormattable)value).ToString(null, NumberFormatInfo.InvariantInfo));
//				}
				if (value is uint)
					b.Append("u");
				else if (value is ulong)
					b.Append("UL");
				else if (value is long)
					b.Append("L");
				writer(b.ToString(), BoxedTextColor.Number);
				column += b.Length;
			} else {
				s = value.ToString();
				writer(s, CSharpMetadataTextColorProvider.Instance.GetColor(value));
				column += s.Length;
			}
		}
		
		/// <summary>
		/// Gets the escape sequence for the specified character within a char literal.
		/// Does not include the single quotes surrounding the char literal.
		/// </summary>
		public static string ConvertCharLiteral(char ch)
		{
			if (ch == '\'') {
				return "\\'";
			}
			return ConvertChar(ch);
		}
		
		/// <summary>
		/// Gets the escape sequence for the specified character.
		/// </summary>
		/// <remarks>This method does not convert ' or ".</remarks>
		static string ConvertChar(char ch)
		{
			switch (ch) {
				case '\\':
					return "\\\\";
				case '\0':
					return "\\0";
				case '\a':
					return "\\a";
				case '\b':
					return "\\b";
				case '\f':
					return "\\f";
				case '\n':
					return "\\n";
				case '\r':
					return "\\r";
				case '\t':
					return "\\t";
				case '\v':
					return "\\v";
				default:
					if (char.IsControl(ch) || char.IsSurrogate(ch) ||
					    // print all uncommon white spaces as numbers
					    (char.IsWhiteSpace(ch) && ch != ' ')) {
						return "\\u" + ((int)ch).ToString("x4");
					} else {
						return ch.ToString();
					}
			}
		}

		static void AppendChar(StringBuilder sb, char ch)
		{
			switch (ch) {
				case '\\':
					sb.Append("\\\\");
					break;
				case '\0':
					sb.Append("\\0");
					break;
				case '\a':
					sb.Append("\\a");
					break;
				case '\b':
					sb.Append("\\b");
					break;
				case '\f':
					sb.Append("\\f");
					break;
				case '\n':
					sb.Append("\\n");
					break;
				case '\r':
					sb.Append("\\r");
					break;
				case '\t':
					sb.Append("\\t");
					break;
				case '\v':
					sb.Append("\\v");
					break;
				default:
					if (char.IsControl(ch) || char.IsSurrogate(ch) ||
					    // print all uncommon white spaces as numbers
					    (char.IsWhiteSpace(ch) && ch != ' ')) {
						sb.Append("\\u");
						sb.Append(((int)ch).ToString("x4"));
					} else {
						sb.Append(ch);
					}
					break;
			}
		}
		
		/// <summary>
		/// Converts special characters to escape sequences within the given string.
		/// </summary>
		public static string ConvertString(string str)
		{
			int i = 0;
			for (; ; i++) {
				if (i >= str.Length)
					return str;
				char c = str[i];
				switch (c) {
				case '"':
				case '\\':
				case '\0':
				case '\a':
				case '\b':
				case '\f':
				case '\n':
				case '\r':
				case '\t':
				case '\v':
					goto escapeChars;
				default:
					if (char.IsControl(c) || char.IsSurrogate(c) || (char.IsWhiteSpace(c) && c != ' '))
						goto escapeChars;
					break;
				}
			}

escapeChars:
			StringBuilder sb = new StringBuilder();
			if (i > 0)
				sb.Append(str, 0, i);
			for (; i < str.Length; i++) {
				char ch = str[i];
				if (ch == '"') {
					sb.Append("\\\"");
				} else {
					AppendChar(sb, ch);
				}
			}
			return sb.ToString();
		}
		
		public override void WritePrimitiveType(string type)
		{
			textWriter.Write(type);
			column += type.Length;
			if (type == "new") {
				textWriter.Write("()");
				column += 2;
			}
		}
		
		public override void StartNode(AstNode node)
		{
			// Write out the indentation, so that overrides of this method
			// can rely use the current output length to identify the position of the node
			// in the output.
			WriteIndentation();
		}
		
		public override void EndNode(AstNode node)
		{
		}
	}
}
