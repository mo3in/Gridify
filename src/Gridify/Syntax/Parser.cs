using System.Collections.Generic;
using System.Linq;

namespace Gridify.Syntax;

internal class Parser
{
   private readonly List<string> _diagnostics = new();
   private readonly SyntaxToken[] _tokens;
   private int _position;

   private readonly SyntaxKind[] _operators =
   {
      SyntaxKind.Equal,
      SyntaxKind.Like,
      SyntaxKind.NotEqual,
      SyntaxKind.NotLike,
      SyntaxKind.GreaterThan,
      SyntaxKind.LessThan,
      SyntaxKind.GreaterOrEqualThan,
      SyntaxKind.LessOrEqualThan,
      SyntaxKind.StartsWith,
      SyntaxKind.EndsWith,
      SyntaxKind.NotStartsWith,
      SyntaxKind.NotEndsWith,
      SyntaxKind.CustomOperator
   };

   public Parser(string text, IEnumerable<IGridifyOperator> customOperators)
   {
      var tokens = new List<SyntaxToken>();
      var lexer = new Lexer(text, customOperators);
      SyntaxToken token;
      do
      {
         token = lexer.NextToken();
         if (token.Kind != SyntaxKind.WhiteSpace) tokens.Add(token);
      } while (token.Kind != SyntaxKind.End);

      _tokens = tokens.ToArray();
      _diagnostics.AddRange(lexer.Diagnostics);
   }

   private SyntaxToken Current => Peek(0);

   private SyntaxToken Peek(int offset)
   {
      var index = _position + offset;
      return index >= _tokens.Length ? _tokens[_tokens.Length - 1] : _tokens[index];
   }

   public SyntaxTree Parse()
   {
      var expression = ParseTerm();
      var end = Match(SyntaxKind.End, GetExpectation(expression.Kind));
      return new SyntaxTree(_diagnostics, expression, end);
   }

   private SyntaxKind GetExpectation(SyntaxKind kind)
   {
      switch (kind)
      {
         case SyntaxKind.FieldExpression or SyntaxKind.FieldToken:
            return SyntaxKind.Operator;
         case SyntaxKind.ValueExpression or SyntaxKind.ValueToken:
            return SyntaxKind.End;
         case SyntaxKind.And or SyntaxKind.Or:
            return SyntaxKind.FieldToken;
         default:
         {
            if (_operators.Contains(kind) || kind == SyntaxKind.BinaryExpression)
               return SyntaxKind.ValueToken;

            return SyntaxKind.End;
         }
      }
   }

   private ExpressionSyntax ParseTerm()
   {
      var left = ParseFactor();
      var binaryKinds = new[]
      {
         SyntaxKind.And,
         SyntaxKind.Or
      };

      while (binaryKinds.Contains(Current.Kind))
      {
         var operatorToken = NextToken();
         var right = ParseFactor();
         left = new BinaryExpressionSyntax(left, operatorToken, right);
      }

      return left;
   }

   private ExpressionSyntax ParseFactor()
   {
      var left = ParsePrimaryExpression();


      while (_operators.Contains(Current.Kind))
      {
         var operatorToken = NextToken();
         var right = ParseValueExpression();
         left = new BinaryExpressionSyntax(left, operatorToken, right);
      }

      return left;
   }

   private ExpressionSyntax ParseValueExpression()
   {
      // field=
      if (Current.Kind != SyntaxKind.ValueToken)
         return new ValueExpressionSyntax(new SyntaxToken(), false, true);

      var valueToken = Match(SyntaxKind.ValueToken);
      var isCaseInsensitive = TryMatch(SyntaxKind.CaseInsensitive, out _);
      return new ValueExpressionSyntax(valueToken, isCaseInsensitive, false);
   }

   private SyntaxToken NextToken()
   {
      var current = Current;
      _position++;
      return current;
   }

   private bool TryMatch(SyntaxKind kind, out SyntaxToken token)
   {
      if (Current.Kind != kind)
      {
         token = Current;
         return false;
      }

      token = NextToken();
      return true;
   }

   private SyntaxToken Match(SyntaxKind kind, SyntaxKind? expectation = null)
   {
      if (Current.Kind == kind)
         return NextToken();

      expectation ??= kind;

      if (!_diagnostics.Any(q => q.StartsWith("Unexpected token")))
         _diagnostics.Add($"Unexpected token <{Current.Kind}> at index {Current.Position}, expected <{expectation}>");

      return new SyntaxToken(kind, Current.Position, Current.Text);
   }

   private ExpressionSyntax ParsePrimaryExpression()
   {
      if (Current.Kind != SyntaxKind.OpenParenthesisToken) return ParseFieldExpression();

      var left = NextToken();
      var expression = ParseTerm();
      var right = Match(SyntaxKind.CloseParenthesis);
      return new ParenthesizedExpressionSyntax(left, expression, right);
   }

   private ExpressionSyntax ParseFieldExpression()
   {
      var fieldToken = Match(SyntaxKind.FieldToken);

      return TryMatch(SyntaxKind.FieldIndexToken, out var fieldSyntaxToken)
         ? new FieldExpressionSyntax(fieldToken, int.Parse(fieldSyntaxToken.Text))
         : new FieldExpressionSyntax(fieldToken);
   }
}
