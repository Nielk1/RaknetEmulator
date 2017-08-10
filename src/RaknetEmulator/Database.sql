CREATE TABLE Games (
    rowId       INTEGER      PRIMARY KEY
                             NOT NULL,
    rowPW       VARCHAR (16) NOT NULL,
    addr        VARCHAR (21) NOT NULL,
    lastUpdate  DATETIME     NOT NULL,
    timeoutSec  INTEGER      NOT NULL,
    clientReqId INTEGER      NOT NULL,
    gameId      VARCHAR (31) NOT NULL
);


CREATE TABLE CustomGameDataField (
    gameCustFieldId INTEGER      PRIMARY KEY,
    gameRowId       INTEGER      NOT NULL,
    [key]           VARCHAR (15) NOT NULL,
    value           STRING (255) NOT NULL,
    UNIQUE (
        gameRowId ASC,
        [key] ASC
    ),
    FOREIGN KEY (
        gameRowId
    )
    REFERENCES Games (rowId) 
);
