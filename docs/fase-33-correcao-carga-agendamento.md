# Queda do circuito ao abrir novo agendamento

## Diagnóstico — 06/11/2026

Na release `aa8fe37`, a API respondeu corretamente à listagem do formulário
compartilhado de novo agendamento. A página solicitava até 100 pacientes e
devolvia a resposta por JS interop como um único objeto JSON ao servidor Blazor.
Com 45 pacientes, o envelope da resposta já tinha 33.738 bytes UTF-8, acima dos
32.768 bytes padrão de mensagem recebida pelo circuito. A serialização externa
de JS interop acrescenta mais bytes.

A navegação autenticada como Administrador reproduziu os mesmos erros de
desconexão enviados pelo proprietário. A falha ocorre durante a lista de
pacientes, antes da carga do catálogo e dos profissionais. Após o timeout, o
tratamento de erro libera o skeleton, mas a inicialização ficou incompleta.
Isso explica a concentração do problema em novo agendamento. Não é evidência
de ausência dos pacientes no banco nem de falha de CORS.

## Correção

Somente a resposta de pacientes desse formulário usa `IJSStreamReference`.
O retorno inicial contém a referência ao Blob; o framework transporta o JSON
em blocos compatíveis com o circuito. O leitor usa desserialização assíncrona,
limite total de 1 MiB e liberação das referências e do stream também em falha.
Autorização, cookies, `no-store`, erros HTTP e paginação existente são mantidos.
Não há aumento do limite global SignalR, migration ou alteração de dados.

A chamada tipada `InvokeAsync<IJSStreamReference>` faz o encapsulamento no
framework. A função JavaScript precisa devolver o Blob diretamente, sem chamar
`DotNet.createJSStreamReference` outra vez. O teste JavaScript exige esse tipo de
retorno; um mock de referência pronta não cobre esse contrato de interoperabilidade.

O mecanismo é o streaming de JS para .NET documentado pela
[Microsoft para Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-dotnet-from-javascript?view=aspnetcore-10.0#stream-from-javascript-to-net).

## Regressão

- JavaScript: resposta sintética maior que 32 KiB devolve somente uma referência
  pequena, preserva conteúdo e `credentials: include`/`no-store`; HTTP 401
  continua sendo falha de sessão.
- .NET: desserialização de 100 pacientes sintéticos acima do limite de mensagem,
  propagação de erro 401, rejeição acima de 1 MiB e descarte das referências.
- Os testes JavaScript entram nos workflows de qualidade e release. Os testes
  .NET integram a suíte Web portátil já executada nesses workflows.
- A conferência visual deve repetir a rota autenticada no navegador integrado,
  sem gravar consulta ou efetuar pagamento. Não versionar respostas de pacientes,
  capturas com dados pessoais ou credenciais.
