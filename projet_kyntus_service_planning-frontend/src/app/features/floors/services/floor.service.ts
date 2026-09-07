import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, retry } from 'rxjs/operators';
import { Floor } from '../models/floor.model';
import { environment } from '../../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class FloorService {
  private apiUrl = `${environment.apiUrl}/floor`;

  constructor(private http: HttpClient) {}

  getAllFloors(): Observable<Floor[]> {
  return this.http.get<Floor[]>(this.apiUrl).pipe(
    retry({ count: 3, delay: 2000 }),
    catchError(err => {
      console.error('Erreur getAllFloors:', err);
      return throwError(() => err);
    })
  );
}

  getFloorById(id: number): Observable<Floor> {
    return this.http.get<Floor>(`${this.apiUrl}/${id}`);
  }

  createFloor(dto: any): Observable<Floor> {
    return this.http.post<Floor>(this.apiUrl, dto);
  }

  updateFloor(id: number, dto: any): Observable<Floor> {
    return this.http.put<Floor>(`${this.apiUrl}/${id}`, dto);
  }

  deleteFloor(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}