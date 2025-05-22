# SD-Threads & Sockets

## 📌 Visão Geral do Projeto
Esta é a primeira fase de um projeto universitário de sistemas distribuídos que implementa um sistema de comunicação baseado em sockets com múltiplos componentes. O projeto demonstra conceitos de computação distribuída, gestão de threads e comunicação via sockets em C#.

## 🏗️ Arquitetura
O projeto é composto por três componentes principais que colaboram para formar um sistema distribuído:

| Componente   | Descrição                                                                                                                    |
|--------------|------------------------------------------------------------------------------------------------------------------------------|
| **Wavy**     | Componente cliente que simula um sensor em alto mar, gerando e enviando dados para o Afregador                                |
| **Agregador**| Componente que recolhe e acumula dados de múltiplas instâncias Wavy, enviando-os periodicamente para o Servidor                |
| **Servidor** | Componente que recebe e armazena centralizadamente todos os dados enviados pelo Afregador                                    |

## ⚙️ Configuração
Os ficheiros de configuração estão na pasta `Config`:

- `config_agr.csv`: Configuração para o componente Agregador  
- `config_wavy.csv`: Configuração para o componente Wavy

## 🚀 Primeiros Passos

### Pré-requisitos
- .NET SDK (compatível com o framework alvo do projeto)  
- Ambiente Windows (para executar os scripts batch)

### Execução da Aplicação
O projeto inclui scripts batch para simplificar o arranque:

- Utilize `startAll.bat` para iniciar todos os componentes em simultâneo  
- Alternativamente, inicie apenas um componente:
  - `newAgr.bat`: Inicia o Agregador  
  - `newWavy.bat`: Inicia o cliente Wavy

## 💻 Desenvolvimento
Cada componente é um projeto C# distinto com o seu próprio ficheiro `.sln`. Abra o respetivo `.sln` no Visual Studio para desenvolver.

## 🔄 Fluxo de Comunicação
```
Cliente Wavy → Agregador → Servidor
```

## 📊 Funcionalidades
- Comunicação por sockets entre componentes distribuídos  
- Processamento de dados multi-thread  
- Parâmetros do sistema configuráveis  
- Scripts batch para fácil deployment e testes

## 📝 Licença
[Insira aqui a sua licença]

## 🤝 Contribuição
[Diretrizes de contribuição, se aplicável]