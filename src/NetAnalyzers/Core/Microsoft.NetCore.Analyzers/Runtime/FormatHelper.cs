// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.Text;

namespace Microsoft.NetCore.Analyzers.Runtime
{
    internal static class FormatHelper
    {
        private enum FormatStringTokenizationState
        {
            FixedText,
            FormatItemStart,
            FormatItemIndex,
            FormatItemAttributes,
            FormatItemEnd
        }

        public const char OpenBrace = '{', CloseBrace = '}';

        public static int CountFormatItems(in ReadOnlySpan<char> formatString)
        {
            // 4396
            // 4431
            var tokenCount = 0;
            var state = FormatStringTokenizationState.FixedText;
            var currentFormatItemIndexSB = new StringBuilder();

            for (var i = 0; i < formatString.Length; i++)
            {
                var c = formatString[i];

                void UpdateTokenCount()
                {
                    var currentFormatItemCount = int.Parse(currentFormatItemIndexSB.ToString()) + 1;

                    if (currentFormatItemCount > tokenCount)
                    {
                        tokenCount = currentFormatItemCount;
                    }

                    currentFormatItemIndexSB.Clear();

                    switch (c)
                    {
                        case CloseBrace:
                            state = FormatStringTokenizationState.FormatItemEnd;
                            break;

                        default:
                            state = FormatStringTokenizationState.FormatItemAttributes;
                            break;
                    }
                }

                switch (state)
                {
                    case FormatStringTokenizationState.FixedText:
                        if (c == OpenBrace)
                        {
                            // This is the start of a potential format item.
                            state = FormatStringTokenizationState.FormatItemStart;
                        }

                        continue;

                    case FormatStringTokenizationState.FormatItemStart:
                        if (c == OpenBrace)
                        {
                            // This is actually an escape sequence.
                            state = FormatStringTokenizationState.FixedText;
                        }
                        else if (char.IsDigit(c))
                        {
                            currentFormatItemIndexSB.Append(c);

                            state = FormatStringTokenizationState.FormatItemIndex;
                        }
                        else
                        {
                            UpdateTokenCount();
                        }

                        continue;

                    case FormatStringTokenizationState.FormatItemIndex:
                        if (char.IsDigit(c))
                        {
                            currentFormatItemIndexSB.Append(c);
                        }
                        else
                        {
                            UpdateTokenCount();
                        }

                        continue;

                    case FormatStringTokenizationState.FormatItemAttributes:
                        if (c == CloseBrace)
                        {
                            state = FormatStringTokenizationState.FormatItemEnd;
                        }

                        continue;

                    case FormatStringTokenizationState.FormatItemEnd:
                        if (c == CloseBrace)
                        {
                            // This is actually an escape sequence. Not really sure this can ever
                            // be part of a valid attribute, but sending back anyway.
                            state = FormatStringTokenizationState.FormatItemAttributes;
                        }
                        else
                        {
                            // In this case, we don't really handle this char from the stream, so
                            // rewind just in case it's another format item immediately after, then
                            // go back to literal.
                            i--;
                            state = FormatStringTokenizationState.FixedText;
                        }

                        continue;
                }
            }

            return tokenCount;
        }
    }
}
