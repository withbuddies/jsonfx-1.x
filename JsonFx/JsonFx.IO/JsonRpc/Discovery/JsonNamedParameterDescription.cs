#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2009 Stephen M. McKamey

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
using System.ComponentModel;
using System.Reflection;

using JsonFx.Json;

namespace JsonFx.JsonRpc.Discovery
{
	public class JsonNamedParameterDescription : JsonParameterDescription
	{
		#region Fields

		private string name = null;

		#endregion Fields

		#region Init

		public JsonNamedParameterDescription() { }

		/// <summary>
		/// Ctor.
		/// </summary>
		internal JsonNamedParameterDescription(ParameterInfo param) : base(param)
		{
			if (param == null)
				return;

			this.Name = param.Name;
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets and sets a simple name for the parameter.
		/// </summary>
		/// <remarks>
		/// REQUIRED. A String value that provides a simple name for parameter.
		/// </remarks>
		[JsonName("name")]
		public string Name
		{
			get { return this.name; }
			set { this.name = value; }
		}

		#endregion Properties
	}
}
