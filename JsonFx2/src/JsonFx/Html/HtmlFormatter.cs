#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2010 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using JsonFx.IO;
using JsonFx.Serialization;
using JsonFx.Serialization.Resolvers;
using JsonFx.Xml;

namespace JsonFx.Html
{
	/// <summary>
	/// Outputs markup text from an input stream of tokens
	/// </summary>
	public class HtmlFormatter : ITextFormatter<MarkupTokenType>
	{
		#region EmptyAttributeType

		public enum EmptyAttributeType
		{
			/// <summary>
			/// HTML-style empty attributes do not emit a quoted string
			/// </summary>
			/// <remarks>
			/// http://www.w3.org/TR/html5/syntax.html#attributes-0
			/// </remarks>
			Html,

			/// <summary>
			/// XHTML-style empty attributes repeat the attribute name as its value
			/// </summary>
			/// <remarks>
			/// http://www.w3.org/TR/xhtml-media-types/#C_10
			/// http://www.w3.org/TR/xhtml1/#C_10
			/// http://www.w3.org/TR/html5/the-xhtml-syntax.html
			/// </remarks>
			Xhtml,

			/// <summary>
			/// XML-style empty attributes emit an empty quoted string
			/// </summary>
			/// <remarks>
			/// http://www.w3.org/TR/xml/#sec-starttags
			/// </remarks>
			Xml
		}

		#endregion EmptyAttributeType

		#region Constants

		private const string ErrorUnexpectedToken = "Unexpected token ({0})";

		#endregion Constants

		#region Fields

		private readonly DataWriterSettings Settings;
		private readonly PrefixScopeChain ScopeChain = new PrefixScopeChain();

		public bool canonicalAttributes;
		private EmptyAttributeType emptyAttributes;
		private bool encodeNonAscii;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public HtmlFormatter(DataWriterSettings settings)
		{
			if (settings == null)
			{
				throw new ArgumentNullException("settings");
			}

			this.Settings = settings;
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets and sets a value indicating if should canonicalize element attributes
		/// </summary>
		/// <remarks>
		/// http://www.w3.org/TR/xml-c14n#DocumentOrder
		/// </remarks>
		public bool CanonicalAttributes
		{
			get { return this.canonicalAttributes; }
			set { this.canonicalAttributes = value; }
		}

		/// <summary>
		/// Gets and sets a value indicating how should emit empty attributes
		/// </summary>
		public EmptyAttributeType EmptyAttributes
		{
			get { return this.emptyAttributes; }
			set { this.emptyAttributes = value; }
		}

		/// <summary>
		/// Gets and sets a value indicating if should encode text chars above the ASCII range
		/// </summary>
		/// <remarks>
		/// This option can help when the output is being embedded within an unknown encoding
		/// </remarks>
		public bool EncodeNonAscii
		{
			get { return this.encodeNonAscii; }
			set { this.encodeNonAscii = value; }
		}

		#endregion Properties

		#region ITextFormatter<T> Methods

		/// <summary>
		/// Formats the token sequence as a string
		/// </summary>
		/// <param name="tokens"></param>
		public string Format(IEnumerable<Token<MarkupTokenType>> tokens)
		{
			using (StringWriter writer = new StringWriter())
			{
				this.Format(writer, tokens);

				return writer.GetStringBuilder().ToString();
			}
		}

		/// <summary>
		/// Formats the token sequence to the writer
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="tokens"></param>
		public void Format(TextWriter writer, IEnumerable<Token<MarkupTokenType>> tokens)
		{
			if (tokens == null)
			{
				throw new ArgumentNullException("tokens");
			}

			IStream<Token<MarkupTokenType>> stream = new Stream<Token<MarkupTokenType>>(tokens);

			PrefixScopeChain.Scope scope = null;
			while (!stream.IsCompleted)
			{
				Token<MarkupTokenType> token = stream.Peek();
				switch (token.TokenType)
				{
					case MarkupTokenType.ElementBegin:
					{
						if (scope == null)
						{
							scope = new PrefixScopeChain.Scope();
						}

						if (this.ScopeChain.ContainsNamespace(token.Name.NamespaceUri) ||
							(String.IsNullOrEmpty(token.Name.NamespaceUri) && !this.ScopeChain.ContainsPrefix(String.Empty)))
						{
							string prefix = this.ScopeChain.GetPrefix(token.Name.NamespaceUri, false);
							scope.TagName = new DataName(token.Name.LocalName, prefix, token.Name.NamespaceUri);
						}
						else
						{
							scope[token.Name.Prefix] = token.Name.NamespaceUri;
							scope.TagName = token.Name;
						}

						this.ScopeChain.Push(scope);

						MarkupTagType tagType = (token.Value is MarkupTagType) ?
							(MarkupTagType)token.Value :
							MarkupTagType.BeginTag;

						stream.Pop();
						token = stream.Peek();

						IDictionary<DataName, Token<MarkupTokenType>> attributes = null;
						while (!stream.IsCompleted && token.TokenType == MarkupTokenType.Attribute)
						{
							if (attributes == null)
							{
								attributes = this.canonicalAttributes ?
									(IDictionary<DataName, Token<MarkupTokenType>>)new SortedList<DataName, Token<MarkupTokenType>>() :
									(IDictionary<DataName, Token<MarkupTokenType>>)new Dictionary<DataName, Token<MarkupTokenType>>();
							}
							DataName attrName = token.Name;

							string prefix = this.ScopeChain.EnsurePrefix(attrName.Prefix, attrName.NamespaceUri);
							if (prefix != attrName.Prefix)
							{
								attrName = new DataName(attrName.LocalName, prefix, attrName.NamespaceUri, true);
							}

							if (!this.ScopeChain.ContainsNamespace(attrName.NamespaceUri) &&
								(!String.IsNullOrEmpty(token.Name.NamespaceUri) || this.ScopeChain.ContainsPrefix(String.Empty)))
							{
								scope[prefix] = attrName.NamespaceUri;
							}

							stream.Pop();
							token = stream.Peek();

							attributes[attrName] = token;

							stream.Pop();
							token = stream.Peek();
						}

						this.WriteTag(writer, tagType, scope.TagName, attributes, scope);

						scope = null;
						break;
					}
					case MarkupTokenType.ElementEnd:
					{
						this.WriteTag(writer, MarkupTagType.EndTag, token.Name, null, null);

						if (this.ScopeChain.HasScope &&
							this.ScopeChain.Peek().TagName == token.Name)
						{
							this.ScopeChain.Pop();
						}
						else
						{
							// TODO: decide how to manage this
						}

						stream.Pop();
						token = stream.Peek();
						break;
					}
					case MarkupTokenType.TextValue:
					case MarkupTokenType.Whitespace:
					{
						this.WriteCharData(writer, token.ValueAsString());

						stream.Pop();
						token = stream.Peek();
						break;
					}
					case MarkupTokenType.UnparsedBlock:
					{
						this.WriteUnparsedBlock(writer, token.Name.LocalName, token.ValueAsString());

						stream.Pop();
						token = stream.Peek();
						break;
					}
					default:
					{
						throw new TokenException<MarkupTokenType>(
							token,
							String.Format(ErrorUnexpectedToken, token.TokenType));
					}
				}
			}
		}

		#endregion ITextFormatter<T> Methods

		#region Write Methods

		private void WriteTag(
			TextWriter writer,
			MarkupTagType type,
			DataName tagName,
			IEnumerable<KeyValuePair<DataName, Token<MarkupTokenType>>> attributes,
			IEnumerable<KeyValuePair<string, string>> prefixDeclarations)
		{
			string tagPrefix = this.ScopeChain.EnsurePrefix(tagName.Prefix, tagName.NamespaceUri);

			// "<"
			writer.Write(MarkupGrammar.OperatorElementBegin);
			if (type == MarkupTagType.EndTag)
			{
				// "/"
				writer.Write(MarkupGrammar.OperatorElementClose);
			}

			if (!String.IsNullOrEmpty(tagPrefix))
			{
				// "prefix:"
				this.WriteLocalName(writer, tagPrefix);
				writer.Write(MarkupGrammar.OperatorPrefixDelim);
			}
			// "local-name"
			this.WriteLocalName(writer, tagName.LocalName);

			// emit all namespaces first, sorted by prefix
			if (prefixDeclarations != null)
			{
				foreach (var declaration in prefixDeclarations)
				{
					this.WriteXmlns(writer, declaration.Key, declaration.Value);
				}
			}

			if (attributes != null)
			{
				foreach (var attribute in attributes)
				{
					// Not sure if this is correct: http://stackoverflow.com/questions/3312390
					// "The namespace name for an unprefixed attribute name always has no value"
					// "The attribute value in a default namespace declaration MAY be empty.
					// This has the same effect, within the scope of the declaration, of there being no default namespace."
					// http://www.w3.org/TR/xml-names/#defaulting
					string attrPrefix = this.ScopeChain.EnsurePrefix(attribute.Key.Prefix, attribute.Key.NamespaceUri);

					this.WriteAttribute(writer, attrPrefix, attribute.Key.LocalName, attribute.Value);
				}
			}

			if (type == MarkupTagType.VoidTag)
			{
				// "/"
				writer.Write(MarkupGrammar.OperatorElementClose);
			}
			// ">"
			writer.Write(MarkupGrammar.OperatorElementEnd);
		}

		private void WriteXmlns(TextWriter writer, string prefix, string namespaceUri)
		{
			// " xmlns"
			writer.Write(MarkupGrammar.OperatorValueDelim);
			this.WriteLocalName(writer, "xmlns");

			if (!String.IsNullOrEmpty(prefix))
			{
				// ":prefix"
				writer.Write(MarkupGrammar.OperatorPrefixDelim);
				this.WriteLocalName(writer, prefix);
			}

			// ="value"
			writer.Write(MarkupGrammar.OperatorPairDelim);
			writer.Write(MarkupGrammar.OperatorStringDelim);
			this.WriteAttributeValue(writer, namespaceUri);
			writer.Write(MarkupGrammar.OperatorStringDelim);
		}

		private void WriteAttribute(TextWriter writer, string prefix, string localName, Token<MarkupTokenType> value)
		{
			// " "
			writer.Write(MarkupGrammar.OperatorValueDelim);

			if (!String.IsNullOrEmpty(prefix))
			{
				// "prefix:"
				this.WriteLocalName(writer, prefix);
				writer.Write(MarkupGrammar.OperatorPrefixDelim);
			}

			// local-name
			this.WriteLocalName(writer, localName);
			string attrValue = value.ValueAsString();

			if (String.IsNullOrEmpty(attrValue))
			{
				switch (this.EmptyAttributes)
				{
					case EmptyAttributeType.Html:
					{
						return;
					}
					case EmptyAttributeType.Xhtml:
					{
						attrValue = localName;
						break;
					}
					case EmptyAttributeType.Xml:
					{
						break;
					}
				}
			}

			// ="value"
			writer.Write(MarkupGrammar.OperatorPairDelim);
			writer.Write(MarkupGrammar.OperatorStringDelim);

			switch (value.TokenType)
			{
				case MarkupTokenType.TextValue:
				case MarkupTokenType.Whitespace:
				{
					this.WriteAttributeValue(writer, attrValue);
					break;
				}
				case MarkupTokenType.UnparsedBlock:
				{
					this.WriteUnparsedBlock(writer, value.Name.LocalName, attrValue);
					break;
				}
				default:
				{
					throw new TokenException<MarkupTokenType>(
						value,
						String.Format(HtmlFormatter.ErrorUnexpectedToken, value));
				}
			}

			writer.Write(MarkupGrammar.OperatorStringDelim);
		}

		/// <summary>
		/// Emits a valid XML local-name (i.e. encodes invalid chars including ':')
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="value"></param>
		/// <remarks>
		/// Explicitly escaping ':' to maintain compatibility with XML Namespaces.
		/// From XML 1.0, 5th ed. http://www.w3.org/TR/xml/#sec-common-syn
		///		Name			= NameStartChar (NameChar)*
		///		NameStartChar	= ":"
		///						| [A-Z]
		///						| "_"
		///						| [a-z]
		///						| [#xC0-#xD6]
		///						| [#xD8-#xF6]
		///						| [#xF8-#x2FF]
		///						| [#x370-#x37D]
		///						| [#x37F-#x1FFF]
		///						| [#x200C-#x200D]
		///						| [#x2070-#x218F]
		///						| [#x2C00-#x2FEF]
		///						| [#x3001-#xD7FF]
		///						| [#xF900-#xFDCF]
		///						| [#xFDF0-#xFFFD]
		///						| [#x10000-#xEFFFF]
		///		NameChar		= NameStartChar
		///						| "-"
		///						| "."
		///						| [0-9]
		///						| #xB7
		///						| [#x0300-#x036F]
		///						| [#x203F-#x2040]
		/// </remarks>
		private void WriteLocalName(TextWriter writer, string value)
		{
			int start = 0,
				length = value.Length;

			for (int i=start; i<length; i++)
			{
				char ch = value[i];

				if ((ch >= 'a' && ch <= 'z') ||
					(ch >= 'A' && ch <= 'Z') ||
					(ch == '_') ||
					(ch >= '\u00C0' && ch <= '\u00D6') ||
					(ch >= '\u00D8' && ch <= '\u00F6') ||
					(ch >= '\u00F8' && ch <= '\u02FF') ||
					(ch >= '\u0370' && ch <= '\u037D') ||
					(ch >= '\u037F' && ch <= '\u1FFF') ||
					(ch >= '\u200C' && ch <= '\u200D') ||
					(ch >= '\u2070' && ch <= '\u218F') ||
					(ch >= '\u2C00' && ch <= '\u2FEF') ||
					(ch >= '\u3001' && ch <= '\uD7FF') ||
					(ch >= '\uF900' && ch <= '\uFDCF') ||
					(ch >= '\uFDF0' && ch <= '\uFFFD'))
				{
					// purposefully leaving out ':' to implement namespace prefixes
					// and cannot represent [#x10000-#xEFFFF] as single char so this will incorrectly escape
					continue;
				}

				if ((i > 0) &&
					((ch >= '0' && ch <= '9') ||
					(ch == '-') ||
					(ch == '.') ||
					(ch == '\u00B7') ||
					(ch >= '\u0300' && ch <= '\u036F') ||
					(ch >= '\u203F' && ch <= '\u2040')))
				{
					// these chars are only valid after initial char
					continue;
				}

				if (i > start)
				{
					// copy any leading unescaped chunk
					writer.Write(value.Substring(start, i-start));
				}
				start = i+1;

				// use XmlSerializer-hex-style encoding of UTF-16
				writer.Write("_x");
				writer.Write(Char.ConvertToUtf32(value, i).ToString("X4"));
				writer.Write("_");
			}

			if (length > start)
			{
				// copy any trailing unescaped chunk
				writer.Write(value.Substring(start, length-start));
			}
		}

		/// <summary>
		/// Emits valid XML character data
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="value"></param>
		/// <remarks>
		/// From XML 1.0, 5th ed. http://www.w3.org/TR/xml/#syntax
		///		CharData is defined as all chars except less-than ('&lt;'), ampersand ('&amp;'), the sequence "]]>", optionally encoding greater-than ('>').
		///	
		///	Rather than detect "]]>", this simply encodes all '>'.
		///	From XML 1.0, 5th ed. http://www.w3.org/TR/xml/#sec-line-ends
		///		"the XML processor must behave as if it normalized all line breaks in external parsed entities (including the document entity) on input, before parsing"
		///	Therefore, this encodes all CR ('\r') chars to preserve them in the final output.
		/// </remarks>
		private void WriteCharData(TextWriter writer, string value)
		{
			int start = 0,
				length = value.Length;

			for (int i=start; i<length; i++)
			{
				char ch = value[i];

				string entity;
				switch (ch)
				{
					case '<':
					{
						entity = "&lt;";
						break;
					}
					case '>':
					{
						entity = "&gt;";
						break;
					}
					case '&':
					{
						entity = "&amp;";
						break;
					}
					//case '\r':
					//{
					//    // http://www.w3.org/TR/xml/#sec-line-ends
					//    entity = "&#xD;";
					//    break;
					//}
					default:
					{
						if (((ch < ' ') && (ch != '\n') && (ch != '\r') && (ch != '\t')) ||
							(this.encodeNonAscii && (ch >= 0x7F)) ||
							((ch >= 0x7F) && (ch <= 0x84)) ||
							((ch >= 0x86) && (ch <= 0x9F)) ||
							((ch >= 0xFDD0) && (ch <= 0xFDEF)))
						{
							// encode all control chars except CRLF/Tab: http://www.w3.org/TR/xml/#charsets
							int utf16 = Char.ConvertToUtf32(value, i);
							entity = String.Concat("&#x", utf16.ToString("X", CultureInfo.InvariantCulture), ';');
							break;
						}
						continue;
					}
				}

				if (i > start)
				{
					// copy any leading unescaped chunk
					writer.Write(value.Substring(start, i-start));
				}
				start = i+1;

				// use XML named entity
				writer.Write(entity);
			}

			if (length > start)
			{
				// copy any trailing unescaped chunk
				writer.Write(value.Substring(start, length-start));
			}
		}

		/// <summary>
		/// Emits valid XML attribute character data
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="value"></param>
		/// <remarks>
		/// From XML 1.0, 5th ed. http://www.w3.org/TR/xml/#syntax
		///		CharData is defined as all chars except less-than ('&lt;'), ampersand ('&amp;'), the sequence "]]>", optionally encoding greater-than ('>').
		///		Attributes should additionally encode double-quote ('"') and single-quote ('\'')
		///	Rather than detect "]]>", this simply encodes all '>'.
		/// </remarks>
		private void WriteAttributeValue(TextWriter writer, string value)
		{
			if (String.IsNullOrEmpty(value))
			{
				return;
			}

			int start = 0,
				length = value.Length;

			for (int i=start; i<length; i++)
			{
				char ch = value[i];

				string entity;
				switch (ch)
				{
					case '<':
					{
						entity = "&lt;";
						break;
					}
					case '>':
					{
						if (this.canonicalAttributes)
						{
							// http://www.w3.org/TR/xml-c14n#ProcessingModel
							continue;
						}

						entity = "&gt;";
						break;
					}
					case '&':
					{
						entity = "&amp;";
						break;
					}
					case '"':
					{
						entity = "&quot;";
						break;
					}
					case '\'':
					{
						if (!this.canonicalAttributes)
						{
							continue;
						}

						// http://www.w3.org/TR/xml-c14n#ProcessingModel
					    entity = "&apos;";
					    break;
					}
					default:
					{
						if ((ch < ' ') ||
							(this.encodeNonAscii && (ch >= 0x7F)) ||
							((ch >= 0x7F) && (ch <= 0x84)) ||
							((ch >= 0x86) && (ch <= 0x9F)) ||
							((ch >= 0xFDD0) && (ch <= 0xFDEF)))
						{
							// encode all control chars: http://www.w3.org/TR/xml/#charsets
							int utf16 = Char.ConvertToUtf32(value, i);
							entity = String.Concat("&#x", utf16.ToString("X", CultureInfo.InvariantCulture), ';');
							break;
						}
						continue;
					}
				}

				if (i > start)
				{
					// copy any leading unescaped chunk
					writer.Write(value.Substring(start, i-start));
				}
				start = i+1;

				// use XML named entity
				writer.Write(entity);
			}

			if (length > start)
			{
				// copy any trailing unescaped chunk
				writer.Write(value.Substring(start, length-start));
			}
		}

		private void WriteUnparsedBlock(TextWriter writer, string name, string value)
		{
			if (name == null)
			{
				writer.Write(value);
			}

			writer.Write(MarkupGrammar.OperatorElementBegin);
			writer.Write(name, value);
			writer.Write(MarkupGrammar.OperatorElementEnd);
		}

		#endregion Write Methods
	}
}
