# Explora - a visual tool to follow a chain of transactions

This is a toy project to try some F# Fable (and Feliz). The explorer fetches transactions from the
Esplora API of mempool.space (falling back to blockstream.info), which needs no API key.

![](/images/view.png)

## How to use

1. Enter a txid in the searchbar and press search
2. Select the transaction to open the transaction details panel on the right side
3. Follow the transactions by clicking on the inputs and outputs


## Give it a try
```
$ cd dist
$ python -m http.server 8080 & $BROWSER http://localhost:8080
```
## How to build
```
$ dotnet tool restore   # installs the Fable compiler pinned in .config/dotnet-tools.json
$ npm ci
$ dotnet fable --run npx vite build
```
The built site in `dist/` uses relative paths, so it works from the root of a server or from a subdirectory.

## How to test
```
$ npm test
```

## How to run 
```
$ python -m http.server 8080
```
and the open the browser in "http://localhost:8080/

-----

## How to watch while developing
```
$ dotnet fable watch --verbose --run npx vite
```

