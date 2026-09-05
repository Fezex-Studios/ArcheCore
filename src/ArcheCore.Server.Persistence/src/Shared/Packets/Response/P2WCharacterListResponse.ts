export interface CharacterSummary{
    CharacterId:number;
    Name:string;
    Level:number;
}

export interface P2WCharacterListResponse{
    AccountId:number;
    Characters:CharacterSummary[];
    
}